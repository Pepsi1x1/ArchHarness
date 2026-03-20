using System.Collections.Concurrent;
using System.Threading.Channels;
using ArchHarness.App.Core;
using ArchHarness.App.Copilot;
using ArchHarness.App.Storage;

namespace ArchHarness.Web.Services;

/// <summary>
/// Hosts a single local run session and rebroadcasts runtime events for web clients.
/// </summary>
public sealed class WebRunSessionManager : IWebRunSessionManager, IAsyncDisposable
{
    private const int MAX_BUFFERED_EVENTS = 256;
    private const string InternalErrorMessage = "The run failed due to an internal error.";
    private const string RUN_STATE_EVENT_KIND = "run-state";
    private const string RUNTIME_PROGRESS_EVENT_KIND = "runtime-progress";
    private const string AGENT_DELTA_EVENT_KIND = "agent-delta";
    private const string RunCanceledMessage = "Run canceled by browser client.";
    private const string RunStoppedMessage = "Run stopped because the local web host is shutting down.";
    private const string COPILOT_SESSION_EVENT_KIND = "copilot-session";
    private const string WEB_HOST_EVENT_SOURCE = "web-host";
    private const string ORCHESTRATOR_EVENT_SOURCE = "orchestrator";
    private const string COPILOT_EVENT_SOURCE = "copilot";

    private readonly OrchestratorRuntime _runtime;
    private readonly IAgentStreamEventStream _agentStreamEventStream;
    private readonly ICopilotSessionEventStream _sessionEventStream;
    private readonly CancellationTokenSource _disposeCts = new CancellationTokenSource();
    private readonly ConcurrentDictionary<Guid, Channel<WebRunEvent>> _subscribers = new ConcurrentDictionary<Guid, Channel<WebRunEvent>>();
    private readonly SemaphoreSlim _runGate = new SemaphoreSlim(1, 1);
    private readonly object _sync = new object();
    private readonly List<WebRunEvent> _bufferedEvents = new List<WebRunEvent>();
    private readonly Task _agentPumpTask;
    private readonly Task _sessionPumpTask;
    private CancellationTokenSource? _activeRunCts;
    private WebRunSnapshot _snapshot = new WebRunSnapshot(false, "idle", null, null, null, null, null, null, null);

    /// <summary>
    /// Initializes a new instance of the <see cref="WebRunSessionManager"/> class.
    /// </summary>
    public WebRunSessionManager(OrchestratorRuntime runtime, IAgentStreamEventStream agentStreamEventStream, ICopilotSessionEventStream sessionEventStream)
    {
        this._runtime = runtime;
        this._agentStreamEventStream = agentStreamEventStream;
        this._sessionEventStream = sessionEventStream;
        this._agentPumpTask = Task.Run(() => this.PumpAgentEventsAsync(this._disposeCts.Token), CancellationToken.None);
        this._sessionPumpTask = Task.Run(() => this.PumpSessionEventsAsync(this._disposeCts.Token), CancellationToken.None);
    }

    /// <inheritdoc />
    public async Task<WebRunSnapshot> StartRunAsync(RunRequest request, CancellationToken cancellationToken)
    {
        await this._runGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            DateTimeOffset startedAt = DateTimeOffset.UtcNow;
            CancellationTokenSource runCts = this.BeginRunSession(
                "starting",
                startedAt,
                null,
                null,
                ResolveSnapshotPrompt(request),
                request.WorkspacePath,
                null);
            this.Publish(new WebRunEvent(startedAt, RUN_STATE_EVENT_KIND, WEB_HOST_EVENT_SOURCE, "Run accepted by local web host."));
            _ = Task.Run(() => this.ExecuteRunAsync(request, runCts), CancellationToken.None);
            return this.GetSnapshot();
        }
        finally
        {
            this._runGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<WebRunSnapshot> ResumeRunAsync(PersistedRunState runState, CancellationToken cancellationToken)
    {
        await this._runGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            CancellationTokenSource runCts = this.BeginRunSession(
                "resuming",
                runState.StartedAtUtc,
                runState.RunId,
                runState.RunDirectory,
                ResolveSnapshotPrompt(runState.Request),
                runState.WorkspaceRoot,
                null);
            this.Publish(new WebRunEvent(DateTimeOffset.UtcNow, RUN_STATE_EVENT_KIND, WEB_HOST_EVENT_SOURCE, $"Run {runState.RunId} resume accepted by local web host."));
            _ = Task.Run(() => this.ExecuteResumeAsync(runState, runCts), CancellationToken.None);
            return this.GetSnapshot();
        }
        finally
        {
            this._runGate.Release();
        }
    }

    /// <inheritdoc />
    public WebRunSnapshot GetSnapshot()
    {
        lock (this._sync)
        {
            return this._snapshot;
        }
    }

    /// <inheritdoc />
    public async Task<WebRunSnapshot> CancelRunAsync()
    {
        CancellationTokenSource? runCts;
        lock (this._sync)
        {
            runCts = this._activeRunCts;
            if (runCts is null || !this._snapshot.IsRunning)
            {
                return this._snapshot;
            }

            this._snapshot = this._snapshot with
            {
                Status = "canceling",
                FailureMessage = null
            };
        }

        this.Publish(new WebRunEvent(DateTimeOffset.UtcNow, RUN_STATE_EVENT_KIND, WEB_HOST_EVENT_SOURCE, "Cancellation requested by browser client."));
        await runCts.CancelAsync().ConfigureAwait(false);
        return this.GetSnapshot();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<WebRunEvent> ReadEventsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Guid subscriberId = Guid.NewGuid();
        Channel<WebRunEvent> channel = Channel.CreateBounded<WebRunEvent>(new BoundedChannelOptions(MAX_BUFFERED_EVENTS)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        lock (this._sync)
        {
            foreach (WebRunEvent evt in this._bufferedEvents)
            {
                channel.Writer.TryWrite(evt);
            }
        }

        this._subscribers[subscriberId] = channel;
        try
        {
            await foreach (WebRunEvent evt in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return evt;
            }
        }
        finally
        {
            this._subscribers.TryRemove(subscriberId, out _);
            channel.Writer.TryComplete();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await this._disposeCts.CancelAsync().ConfigureAwait(false);

        try
        {
            await this._agentPumpTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected when the host is shutting down.
        }

        try
        {
            await this._sessionPumpTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected when the host is shutting down.
        }

        foreach (Channel<WebRunEvent> channel in this._subscribers.Values)
        {
            channel.Writer.TryComplete();
        }

        this._runGate.Dispose();
        this._disposeCts.Dispose();
    }

    private async Task ExecuteRunAsync(RunRequest request, CancellationTokenSource runCts)
    {
        Progress<RuntimeProgressEvent> progress = new Progress<RuntimeProgressEvent>(evt =>
        {
            this.Publish(new WebRunEvent(evt.TimestampUtc, RUNTIME_PROGRESS_EVENT_KIND, evt.Source, evt.Message, Details: evt.Prompt));
            this.UpdateStatus("running", null, null);
        });

        try
        {
            RunArtefacts artefacts = await this._runtime.RunAsync(request, progress, this.OnRunContextEstablished, runCts.Token).ConfigureAwait(false);
            this.CompleteRun("completed", artefacts, null, $"Run {artefacts.RunId} completed.");
        }
        catch (OperationCanceledException) when (runCts.IsCancellationRequested && !this._disposeCts.IsCancellationRequested)
        {
            this.FailRun("canceled", RunCanceledMessage, WEB_HOST_EVENT_SOURCE, RunCanceledMessage);
        }
        catch (OperationCanceledException) when (this._disposeCts.IsCancellationRequested)
        {
            this.FailRun("stopped", "Web host is shutting down.", WEB_HOST_EVENT_SOURCE, RunStoppedMessage);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[WebRunSessionManager] Run failed: {ex}");
            this.FailRun("failed", InternalErrorMessage, ORCHESTRATOR_EVENT_SOURCE, "Run failed.");
        }
        finally
        {
            this.ReleaseRun(runCts);
        }
    }

    private async Task ExecuteResumeAsync(PersistedRunState runState, CancellationTokenSource runCts)
    {
        Progress<RuntimeProgressEvent> progress = new Progress<RuntimeProgressEvent>(evt =>
        {
            this.Publish(new WebRunEvent(evt.TimestampUtc, RUNTIME_PROGRESS_EVENT_KIND, evt.Source, evt.Message, Details: evt.Prompt));
            this.UpdateStatus("running", null, null);
        });

        try
        {
            RunArtefacts artefacts = await this._runtime.ResumeAsync(runState, progress, this.OnRunContextEstablished, runCts.Token).ConfigureAwait(false);
            this.CompleteRun("completed", artefacts, null, $"Run {artefacts.RunId} completed.");
        }
        catch (OperationCanceledException) when (runCts.IsCancellationRequested && !this._disposeCts.IsCancellationRequested)
        {
            this.FailRun("canceled", RunCanceledMessage, WEB_HOST_EVENT_SOURCE, RunCanceledMessage);
        }
        catch (OperationCanceledException) when (this._disposeCts.IsCancellationRequested)
        {
            this.FailRun("stopped", "Web host is shutting down.", WEB_HOST_EVENT_SOURCE, RunStoppedMessage);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[WebRunSessionManager] Resume failed: {ex}");
            this.FailRun("failed", InternalErrorMessage, ORCHESTRATOR_EVENT_SOURCE, "Run failed.");
        }
        finally
        {
            this.ReleaseRun(runCts);
        }
    }

    private static string? ResolveSnapshotPrompt(RunRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.TaskPrompt))
        {
            return request.TaskPrompt;
        }

        return string.IsNullOrWhiteSpace(request.ArchitectureLoopPrompt)
            ? null
            : request.ArchitectureLoopPrompt;
    }

    private CancellationTokenSource BeginRunSession(
        string status,
        DateTimeOffset startedAt,
        string? runId,
        string? runDirectory,
        string? taskPrompt,
        string? workspacePath,
        string? failureMessage)
    {
        if (this._snapshot.IsRunning)
        {
            throw new InvalidOperationException("A run is already active in the local web host.");
        }

        CancellationTokenSource runCts = CancellationTokenSource.CreateLinkedTokenSource(this._disposeCts.Token);
        this.ResetBuffer();

        lock (this._sync)
        {
            this._activeRunCts = runCts;
            this._snapshot = new WebRunSnapshot(
                true,
                status,
                startedAt,
                null,
                runId,
                runDirectory,
                taskPrompt,
                workspacePath,
                failureMessage);
        }

        return runCts;
    }

    private void CompleteRun(string status, RunArtefacts artefacts, string? failureMessage, string message)
    {
        DateTimeOffset completedAt = DateTimeOffset.UtcNow;
        lock (this._sync)
        {
            this._snapshot = this._snapshot with
            {
                IsRunning = false,
                Status = status,
                CompletedAtUtc = completedAt,
                RunId = artefacts.RunId,
                RunDirectory = artefacts.RunDirectory,
                FailureMessage = failureMessage
            };
        }

        this.Publish(new WebRunEvent(completedAt, RUN_STATE_EVENT_KIND, ORCHESTRATOR_EVENT_SOURCE, message, Details: artefacts.RunDirectory));
    }

    private void FailRun(string status, string failureMessage, string source, string message)
    {
        DateTimeOffset completedAt = DateTimeOffset.UtcNow;
        lock (this._sync)
        {
            this._snapshot = this._snapshot with
            {
                IsRunning = false,
                Status = status,
                CompletedAtUtc = completedAt,
                FailureMessage = failureMessage
            };
        }

        this.Publish(new WebRunEvent(completedAt, RUN_STATE_EVENT_KIND, source, message));
    }

    private void ReleaseRun(CancellationTokenSource runCts)
    {
        lock (this._sync)
        {
            if (ReferenceEquals(this._activeRunCts, runCts))
            {
                this._activeRunCts = null;
            }
        }

        runCts.Dispose();
    }

    private async Task PumpAgentEventsAsync(CancellationToken cancellationToken)
    {
        await foreach (AgentStreamDeltaEvent evt in this._agentStreamEventStream.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            this.Publish(new WebRunEvent(
                evt.TimestampUtc,
                AGENT_DELTA_EVENT_KIND,
                evt.AgentRole,
                evt.DeltaContent,
                evt.AgentId,
                evt.AgentRole,
                ContentFormat: evt.ContentFormat,
                StreamKind: evt.StreamKind,
                Title: evt.Title));
        }
    }

    private async Task PumpSessionEventsAsync(CancellationToken cancellationToken)
    {
        await foreach (CopilotSessionLifecycleEvent evt in this._sessionEventStream.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            string message = string.IsNullOrWhiteSpace(evt.Details)
                ? evt.EventType
                : $"{evt.EventType}: {evt.Details}";
            this.Publish(new WebRunEvent(evt.TimestampUtc, COPILOT_SESSION_EVENT_KIND, COPILOT_EVENT_SOURCE, message, SessionId: evt.SessionId, Model: evt.Model, Details: evt.EventType));
        }
    }

    private void Publish(WebRunEvent evt)
    {
        lock (this._sync)
        {
            this._bufferedEvents.Add(evt);
            if (this._bufferedEvents.Count > MAX_BUFFERED_EVENTS)
            {
                this._bufferedEvents.RemoveAt(0);
            }
        }

        foreach (KeyValuePair<Guid, Channel<WebRunEvent>> subscriber in this._subscribers)
        {
            subscriber.Value.Writer.TryWrite(evt);
        }
    }

    private void ResetBuffer()
    {
        lock (this._sync)
        {
            this._bufferedEvents.Clear();
        }
    }

    private void OnRunContextEstablished(string runId, string runDirectory)
    {
        lock (this._sync)
        {
            this._snapshot = this._snapshot with
            {
                RunId = runId,
                RunDirectory = runDirectory
            };
        }

        this.Publish(new WebRunEvent(DateTimeOffset.UtcNow, RUN_STATE_EVENT_KIND, ORCHESTRATOR_EVENT_SOURCE, $"Run {runId} started.", Details: runDirectory));
    }

    private void UpdateStatus(string status, DateTimeOffset? completedAtUtc, string? failureMessage)
    {
        lock (this._sync)
        {
            this._snapshot = this._snapshot with
            {
                IsRunning = true,
                Status = status,
                CompletedAtUtc = completedAtUtc,
                FailureMessage = failureMessage
            };
        }
    }
}
