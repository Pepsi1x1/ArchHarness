using ArchHarness.App.Core;
using ArchHarness.App.Copilot;
using ArchHarness.App.Storage;

namespace ArchHarness.Web.Services;

/// <summary>
/// Executes new and resumed web-hosted runs against the orchestrator runtime.
/// </summary>
public interface IWebRunExecutionRunner
{
    /// <summary>
    /// Executes a new run.
    /// </summary>
    Task ExecuteRunAsync(RunRequest request, CancellationTokenSource runCts, CancellationToken shutdownToken);

    /// <summary>
    /// Executes a resumed run.
    /// </summary>
    Task ExecuteResumeAsync(PersistedRunState runState, CancellationTokenSource runCts, CancellationToken shutdownToken);
}

/// <summary>
/// Default implementation of <see cref="IWebRunExecutionRunner"/>.
/// </summary>
public sealed class WebRunExecutionRunner : IWebRunExecutionRunner
{
    private const string INTERNAL_ERROR_MESSAGE = "The run failed due to an internal error.";
    private const string RUN_STATE_EVENT_KIND = "run-state";
    private const string RUNTIME_PROGRESS_EVENT_KIND = "runtime-progress";
    private const string RUN_CANCELED_MESSAGE = "Run canceled by browser client.";
    private const string RUN_PAUSED_MESSAGE = "Run paused by browser client.";
    private const string RUN_STOPPED_MESSAGE = "Run stopped because the local web host is shutting down.";
    private const string WEB_HOST_EVENT_SOURCE = "web-host";
    private const string ORCHESTRATOR_EVENT_SOURCE = "orchestrator";

    private readonly IOrchestratorRuntime _runtime;
    private readonly IWebRunEventHub _eventHub;
    private readonly IRunStateStore _runStateStore;
    private readonly IWebRunSnapshotStore _snapshotStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebRunExecutionRunner"/> class.
    /// </summary>
    public WebRunExecutionRunner(IOrchestratorRuntime runtime, IWebRunEventHub eventHub, IRunStateStore runStateStore, IWebRunSnapshotStore snapshotStore)
    {
        this._runtime = runtime;
        this._eventHub = eventHub;
        this._runStateStore = runStateStore;
        this._snapshotStore = snapshotStore;
    }

    /// <inheritdoc />
    public async Task ExecuteRunAsync(RunRequest request, CancellationTokenSource runCts, CancellationToken shutdownToken)
    {
        Progress<RuntimeProgressEvent> progress = this.CreateProgress();
        try
        {
            RunArtefacts artefacts = await this._runtime.RunAsync(request, progress, this.OnRunContextEstablished, runCts.Token).ConfigureAwait(false);
            this._snapshotStore.CompleteRun(RunStatuses.COMPLETED, artefacts, null);
            this._eventHub.Publish(new WebRunEvent(DateTimeOffset.UtcNow, RUN_STATE_EVENT_KIND, ORCHESTRATOR_EVENT_SOURCE, $"Run {artefacts.RunId} completed.", Details: artefacts.RunDirectory));
        }
        catch (OperationCanceledException) when (runCts.IsCancellationRequested && !shutdownToken.IsCancellationRequested)
        {
            if (await this.TryPauseRunAsync().ConfigureAwait(false))
            {
                return;
            }

            this._snapshotStore.FailRun(RunStatuses.CANCELED, RUN_CANCELED_MESSAGE);
            this._eventHub.Publish(new WebRunEvent(DateTimeOffset.UtcNow, RUN_STATE_EVENT_KIND, WEB_HOST_EVENT_SOURCE, RUN_CANCELED_MESSAGE));
        }
        catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
        {
            this._snapshotStore.FailRun(RunStatuses.STOPPED, RUN_STOPPED_MESSAGE);
            this._eventHub.Publish(new WebRunEvent(DateTimeOffset.UtcNow, RUN_STATE_EVENT_KIND, WEB_HOST_EVENT_SOURCE, RUN_STOPPED_MESSAGE));
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[WebRunExecutionRunner] Run failed: {ex}");
            this._snapshotStore.FailRun(RunStatuses.FAILED, INTERNAL_ERROR_MESSAGE);
            this._eventHub.Publish(new WebRunEvent(DateTimeOffset.UtcNow, RUN_STATE_EVENT_KIND, ORCHESTRATOR_EVENT_SOURCE, "Run failed."));
        }
        finally
        {
            this._snapshotStore.ReleaseRun(runCts);
        }
    }

    /// <inheritdoc />
    public async Task ExecuteResumeAsync(PersistedRunState runState, CancellationTokenSource runCts, CancellationToken shutdownToken)
    {
        Progress<RuntimeProgressEvent> progress = this.CreateProgress();
        try
        {
            RunArtefacts artefacts = await this._runtime.ResumeAsync(runState, progress, this.OnRunContextEstablished, runCts.Token).ConfigureAwait(false);
            this._snapshotStore.CompleteRun(RunStatuses.COMPLETED, artefacts, null);
            this._eventHub.Publish(new WebRunEvent(DateTimeOffset.UtcNow, RUN_STATE_EVENT_KIND, ORCHESTRATOR_EVENT_SOURCE, $"Run {artefacts.RunId} completed.", Details: artefacts.RunDirectory));
        }
        catch (OperationCanceledException) when (runCts.IsCancellationRequested && !shutdownToken.IsCancellationRequested)
        {
            if (await this.TryPauseRunAsync().ConfigureAwait(false))
            {
                return;
            }

            this._snapshotStore.FailRun(RunStatuses.CANCELED, RUN_CANCELED_MESSAGE);
            this._eventHub.Publish(new WebRunEvent(DateTimeOffset.UtcNow, RUN_STATE_EVENT_KIND, WEB_HOST_EVENT_SOURCE, RUN_CANCELED_MESSAGE));
        }
        catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
        {
            this._snapshotStore.FailRun(RunStatuses.STOPPED, RUN_STOPPED_MESSAGE);
            this._eventHub.Publish(new WebRunEvent(DateTimeOffset.UtcNow, RUN_STATE_EVENT_KIND, WEB_HOST_EVENT_SOURCE, RUN_STOPPED_MESSAGE));
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[WebRunExecutionRunner] Resume failed: {ex}");
            this._snapshotStore.FailRun(RunStatuses.FAILED, INTERNAL_ERROR_MESSAGE);
            this._eventHub.Publish(new WebRunEvent(DateTimeOffset.UtcNow, RUN_STATE_EVENT_KIND, ORCHESTRATOR_EVENT_SOURCE, "Run failed."));
        }
        finally
        {
            this._snapshotStore.ReleaseRun(runCts);
        }
    }

    private Progress<RuntimeProgressEvent> CreateProgress()
        => new(evt =>
        {
            this._eventHub.Publish(new WebRunEvent(evt.TimestampUtc, RUNTIME_PROGRESS_EVENT_KIND, evt.Source, evt.Message, Details: RedactProgressDetails(evt.Prompt)));
            this._snapshotStore.UpdateStatus(RunStatuses.RUNNING, null, null);
        });

    private static string? RedactProgressDetails(string? prompt)
        => prompt is null ? null : Redaction.RedactSecrets(prompt);

    private async Task<bool> TryPauseRunAsync()
    {
        if (!this._snapshotStore.IsPauseRequested())
        {
            return false;
        }

        WebRunSnapshot snapshot = this._snapshotStore.GetSnapshot();
        if (string.IsNullOrWhiteSpace(snapshot.RunDirectory))
        {
            return false;
        }

        PersistedRunState? existingState = this._runStateStore.GetState(snapshot.RunDirectory);
        if (existingState is null)
        {
            return false;
        }

        await this._runStateStore.WriteStateAsync(
            snapshot.RunDirectory,
            existingState with
            {
                Status = RunStatuses.PAUSED,
                Phase = RunTerminalPhases.PAUSED,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                FailureMessage = null
            },
            CancellationToken.None).ConfigureAwait(false);

        this._snapshotStore.PauseRun();
        this._eventHub.Publish(new WebRunEvent(DateTimeOffset.UtcNow, RUN_STATE_EVENT_KIND, WEB_HOST_EVENT_SOURCE, RUN_PAUSED_MESSAGE));
        return true;
    }

    private void OnRunContextEstablished(string runId, string runDirectory)
    {
        this._snapshotStore.SetRunContext(runId, runDirectory);
        this._eventHub.Publish(new WebRunEvent(DateTimeOffset.UtcNow, RUN_STATE_EVENT_KIND, ORCHESTRATOR_EVENT_SOURCE, $"Run {runId} started.", Details: runDirectory));
    }
}
