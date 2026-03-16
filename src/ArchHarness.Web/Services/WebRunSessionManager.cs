using System.Collections.Concurrent;
using System.Threading.Channels;
using ArchHarness.App.Core;
using ArchHarness.App.Copilot;

namespace ArchHarness.Web.Services;

/// <summary>
/// Hosts a single local run session and rebroadcasts runtime events for web clients.
/// </summary>
public sealed class WebRunSessionManager : IWebRunSessionManager, IAsyncDisposable
{
    private const int MAX_BUFFERED_EVENTS = 256;

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

            if (this._snapshot.IsRunning)
            {
                throw new InvalidOperationException("A run is already active in the local web host.");
            }

            DateTimeOffset startedAt = DateTimeOffset.UtcNow;
            CancellationTokenSource runCts = CancellationTokenSource.CreateLinkedTokenSource(this._disposeCts.Token);
            this.ResetBuffer();
            lock (this._sync)
            {
                this._activeRunCts = runCts;
            }

            this._snapshot = new WebRunSnapshot(true, "starting", startedAt, null, null, null, request.TaskPrompt, request.WorkspacePath, null);
            this.Publish(new WebRunEvent(startedAt, "run-state", "web-host", "Run accepted by local web host."));

            _ = Task.Run(() => this.ExecuteRunAsync(request, runCts), CancellationToken.None);
            return this._snapshot;
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

        this.Publish(new WebRunEvent(DateTimeOffset.UtcNow, "run-state", "web-host", "Cancellation requested by browser client."));
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
            this.Publish(new WebRunEvent(evt.TimestampUtc, "runtime-progress", evt.Source, evt.Message, Details: evt.Prompt));
            this.UpdateStatus("running", null, null);
        });

        try
        {
            RunArtefacts artefacts = await this._runtime.RunAsync(request, progress, this.OnRunContextEstablished, runCts.Token).ConfigureAwait(false);
            DateTimeOffset completedAt = DateTimeOffset.UtcNow;
            lock (this._sync)
            {
                this._snapshot = this._snapshot with
                {
                    IsRunning = false,
                    Status = "completed",
                    CompletedAtUtc = completedAt,
                    RunId = artefacts.RunId,
                    RunDirectory = artefacts.RunDirectory,
                    FailureMessage = null
                };
            }

            this.Publish(new WebRunEvent(completedAt, "run-state", "orchestrator", $"Run {artefacts.RunId} completed.", Details: artefacts.RunDirectory));
        }
        catch (OperationCanceledException) when (runCts.IsCancellationRequested && !this._disposeCts.IsCancellationRequested)
        {
            DateTimeOffset completedAt = DateTimeOffset.UtcNow;
            lock (this._sync)
            {
                this._snapshot = this._snapshot with
                {
                    IsRunning = false,
                    Status = "canceled",
                    CompletedAtUtc = completedAt,
                    FailureMessage = "Run canceled by browser client."
                };
            }

            this.Publish(new WebRunEvent(completedAt, "run-state", "web-host", "Run canceled by browser client."));
        }
        catch (OperationCanceledException) when (this._disposeCts.IsCancellationRequested)
        {
            DateTimeOffset completedAt = DateTimeOffset.UtcNow;
            lock (this._sync)
            {
                this._snapshot = this._snapshot with
                {
                    IsRunning = false,
                    Status = "stopped",
                    CompletedAtUtc = completedAt,
                    FailureMessage = "Web host is shutting down."
                };
            }

            this.Publish(new WebRunEvent(completedAt, "run-state", "web-host", "Run stopped because the local web host is shutting down."));
        }
        catch (Exception ex)
        {
            DateTimeOffset completedAt = DateTimeOffset.UtcNow;
            await Console.Error.WriteLineAsync($"[WebRunSessionManager] Run failed: {ex}");
            lock (this._sync)
            {
                this._snapshot = this._snapshot with
                {
                    IsRunning = false,
                    Status = "failed",
                    CompletedAtUtc = completedAt,
                    FailureMessage = "The run failed due to an internal error."
                };
            }

            this.Publish(new WebRunEvent(completedAt, "run-state", "orchestrator", "Run failed."));
        }
        finally
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
    }

    private async Task PumpAgentEventsAsync(CancellationToken cancellationToken)
    {
        await foreach (AgentStreamDeltaEvent evt in this._agentStreamEventStream.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            this.Publish(new WebRunEvent(
                evt.TimestampUtc,
                "agent-delta",
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
            this.Publish(new WebRunEvent(evt.TimestampUtc, "copilot-session", "copilot", message, SessionId: evt.SessionId, Model: evt.Model, Details: evt.EventType));
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

        this.Publish(new WebRunEvent(DateTimeOffset.UtcNow, "run-state", "orchestrator", $"Run {runId} started.", Details: runDirectory));
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