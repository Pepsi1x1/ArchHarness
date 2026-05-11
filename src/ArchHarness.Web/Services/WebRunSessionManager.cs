using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using ArchHarness.App.Storage;

namespace ArchHarness.Web.Services;

/// <summary>
/// Hosts a single local run session and rebroadcasts runtime events for web clients.
/// </summary>
public sealed class WebRunSessionManager : IWebRunSessionManager, IAsyncDisposable
{
    private const string RUN_STATE_EVENT_KIND = "run-state";
    private const string AGENT_DELTA_EVENT_KIND = "agent-delta";
    private const string COPILOT_SESSION_EVENT_KIND = "copilot-session";
    private const string WEB_HOST_EVENT_SOURCE = "web-host";
    private const string COPILOT_EVENT_SOURCE = "copilot";
    private const string PAUSE_REQUESTED_MESSAGE = "Pause requested by browser client.";

    private readonly IWebRunExecutionRunner _executionRunner;
    private readonly IWebRunEventHub _eventHub;
    private readonly IWebRunSnapshotStore _snapshotStore;
    private readonly IAgentStreamEventStream _agentStreamEventStream;
    private readonly ICopilotSessionEventStream _sessionEventStream;
    private readonly CancellationTokenSource _disposeCts = new CancellationTokenSource();
    private readonly SemaphoreSlim _runGate = new SemaphoreSlim(1, 1);
    private readonly Task _agentPumpTask;
    private readonly Task _sessionPumpTask;
    private Task? _activeExecutionTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebRunSessionManager"/> class.
    /// </summary>
    public WebRunSessionManager(
        IWebRunExecutionRunner executionRunner,
        IWebRunEventHub eventHub,
        IWebRunSnapshotStore snapshotStore,
        IAgentStreamEventStream agentStreamEventStream,
        ICopilotSessionEventStream sessionEventStream)
    {
        this._executionRunner = executionRunner;
        this._eventHub = eventHub;
        this._snapshotStore = snapshotStore;
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
            this._eventHub.Reset();
            CancellationTokenSource runCts = this._snapshotStore.BeginRunSession(new WebRunSessionStart(
                this._disposeCts.Token,
                RunStatuses.STARTING,
                startedAt,
                null,
                null,
                ResolveSnapshotPrompt(request),
                request.WorkspacePath,
                null));
            this._eventHub.Publish(new WebRunEvent(startedAt, RUN_STATE_EVENT_KIND, WEB_HOST_EVENT_SOURCE, "Run accepted by local web host."));
            this._activeExecutionTask = Task.Run(() => this._executionRunner.ExecuteRunAsync(request, runCts, this._disposeCts.Token), CancellationToken.None);
            return this._snapshotStore.GetSnapshot();
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
            this._eventHub.Reset();
            CancellationTokenSource runCts = this._snapshotStore.BeginRunSession(new WebRunSessionStart(
                this._disposeCts.Token,
                RunStatuses.RESUMING,
                runState.StartedAtUtc,
                runState.RunId,
                runState.RunDirectory,
                ResolveSnapshotPrompt(runState.Request),
                runState.WorkspaceRoot,
                null));
            this._eventHub.Publish(new WebRunEvent(DateTimeOffset.UtcNow, RUN_STATE_EVENT_KIND, WEB_HOST_EVENT_SOURCE, $"Run {runState.RunId} resume accepted by local web host."));
            this._activeExecutionTask = Task.Run(() => this._executionRunner.ExecuteResumeAsync(runState, runCts, this._disposeCts.Token), CancellationToken.None);
            return this._snapshotStore.GetSnapshot();
        }
        finally
        {
            this._runGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<WebRunSnapshot> RegenerateMegaWikiAsync(PersistedRunState runState, CancellationToken cancellationToken)
    {
        await this._runGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            this._eventHub.Reset();
            CancellationTokenSource runCts = this._snapshotStore.BeginRunSession(new WebRunSessionStart(
                this._disposeCts.Token,
                RunStatuses.RESUMING,
                runState.StartedAtUtc,
                runState.RunId,
                runState.RunDirectory,
                ResolveSnapshotPrompt(runState.Request),
                runState.WorkspaceRoot,
                null));
            this._eventHub.Publish(new WebRunEvent(DateTimeOffset.UtcNow, RUN_STATE_EVENT_KIND, WEB_HOST_EVENT_SOURCE, $"Run {runState.RunId} megawiki regeneration accepted by local web host."));
            this._activeExecutionTask = Task.Run(() => this._executionRunner.ExecuteRegenerateMegaWikiAsync(runState, runCts, this._disposeCts.Token), CancellationToken.None);
            return this._snapshotStore.GetSnapshot();
        }
        finally
        {
            this._runGate.Release();
        }
    }

    /// <inheritdoc />
    public WebRunSnapshot GetSnapshot()
        => this._snapshotStore.GetSnapshot();

    /// <inheritdoc />
    public async Task<WebRunSnapshot> CancelRunAsync()
    {
        CancellationTokenSource? runCts = this._snapshotStore.RequestCancellation();
        if (runCts is null)
        {
            return this._snapshotStore.GetSnapshot();
        }

        this._eventHub.Publish(new WebRunEvent(DateTimeOffset.UtcNow, RUN_STATE_EVENT_KIND, WEB_HOST_EVENT_SOURCE, "Cancellation requested by browser client."));
        await runCts.CancelAsync().ConfigureAwait(false);
        return this._snapshotStore.GetSnapshot();
    }

    /// <inheritdoc />
    public async Task<WebRunSnapshot> PauseRunAsync()
    {
        WebRunSnapshot snapshot = this._snapshotStore.GetSnapshot();
        if (!snapshot.IsRunning)
        {
            return snapshot;
        }

        if (string.IsNullOrWhiteSpace(snapshot.RunId) || string.IsNullOrWhiteSpace(snapshot.RunDirectory))
        {
            throw new InvalidOperationException("The active run cannot be paused until startup completes.");
        }

        CancellationTokenSource? runCts = this._snapshotStore.RequestPause();
        if (runCts is null)
        {
            return this._snapshotStore.GetSnapshot();
        }

        this._eventHub.Publish(new WebRunEvent(DateTimeOffset.UtcNow, RUN_STATE_EVENT_KIND, WEB_HOST_EVENT_SOURCE, PAUSE_REQUESTED_MESSAGE));
        await runCts.CancelAsync().ConfigureAwait(false);
        return this._snapshotStore.GetSnapshot();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<WebRunEvent> ReadEventsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (WebRunEvent evt in this._eventHub.ReadEventsAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return evt;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await this._disposeCts.CancelAsync().ConfigureAwait(false);

        Task? activeExecutionTask = this._activeExecutionTask;
        if (activeExecutionTask is not null)
        {
            try
            {
                await activeExecutionTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected when the host is shutting down.
            }
        }

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

        this._eventHub.CompleteSubscribers();
        this._runGate.Dispose();
        this._disposeCts.Dispose();
    }

    private static string? ResolveSnapshotPrompt(RunRequest request)
    {
        request = RunRequestWorkflowDefaults.Apply(request);
        if (!string.IsNullOrWhiteSpace(request.TaskPrompt))
        {
            return request.TaskPrompt;
        }

        return string.IsNullOrWhiteSpace(request.ArchitectureLoopPrompt)
            ? null
            : request.ArchitectureLoopPrompt;
    }

    private async Task PumpAgentEventsAsync(CancellationToken cancellationToken)
    {
        await foreach (AgentStreamDeltaEvent evt in this._agentStreamEventStream.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            this._eventHub.Publish(new WebRunEvent(
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
            this._eventHub.Publish(new WebRunEvent(evt.TimestampUtc, COPILOT_SESSION_EVENT_KIND, COPILOT_EVENT_SOURCE, message, SessionId: evt.SessionId, Model: evt.Model, Details: evt.EventType));
        }
    }
}
