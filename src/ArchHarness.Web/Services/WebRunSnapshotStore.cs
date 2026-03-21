using ArchHarness.App.Core;
namespace ArchHarness.Web.Services;

/// <summary>
/// Owns mutable run-session snapshot state for the local web host.
/// </summary>
public interface IWebRunSnapshotStore
{
    /// <summary>
    /// Returns the current run snapshot.
    /// </summary>
    WebRunSnapshot GetSnapshot();

    /// <summary>
    /// Begins a new run session and returns the active cancellation source.
    /// </summary>
    CancellationTokenSource BeginRunSession(
        CancellationToken shutdownToken,
        string status,
        DateTimeOffset startedAt,
        string? runId,
        string? runDirectory,
        string? taskPrompt,
        string? workspacePath,
        string? failureMessage);

    /// <summary>
    /// Marks the active run as canceling and returns its cancellation source, if any.
    /// </summary>
    CancellationTokenSource? RequestCancellation();

    /// <summary>
    /// Updates the run context once the orchestrator establishes the run directory.
    /// </summary>
    void SetRunContext(string runId, string runDirectory);

    /// <summary>
    /// Updates the active run status while it is running.
    /// </summary>
    void UpdateStatus(string status, DateTimeOffset? completedAtUtc, string? failureMessage);

    /// <summary>
    /// Marks the run as completed.
    /// </summary>
    void CompleteRun(string status, RunArtefacts artefacts, string? failureMessage);

    /// <summary>
    /// Marks the run as failed, canceled, or stopped.
    /// </summary>
    void FailRun(string status, string failureMessage);

    /// <summary>
    /// Releases the active run token source if it is still current.
    /// </summary>
    void ReleaseRun(CancellationTokenSource runCts);
}

/// <summary>
/// Default implementation of <see cref="IWebRunSnapshotStore"/>.
/// </summary>
public sealed class WebRunSnapshotStore : IWebRunSnapshotStore
{
    private readonly object _sync = new();
    private CancellationTokenSource? _activeRunCts;
    private WebRunSnapshot _snapshot = new(false, RunStatuses.IDLE, null, null, null, null, null, null, null);

    /// <inheritdoc />
    public WebRunSnapshot GetSnapshot()
    {
        lock (this._sync)
        {
            return this._snapshot;
        }
    }

    /// <inheritdoc />
    public CancellationTokenSource BeginRunSession(
        CancellationToken shutdownToken,
        string status,
        DateTimeOffset startedAt,
        string? runId,
        string? runDirectory,
        string? taskPrompt,
        string? workspacePath,
        string? failureMessage)
    {
        lock (this._sync)
        {
            if (this._snapshot.IsRunning)
            {
                throw new InvalidOperationException("A run is already active in the local web host.");
            }

            CancellationTokenSource runCts = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
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
            return runCts;
        }
    }

    /// <inheritdoc />
    public CancellationTokenSource? RequestCancellation()
    {
        lock (this._sync)
        {
            if (this._activeRunCts is null || !this._snapshot.IsRunning)
            {
                return null;
            }

            this._snapshot = this._snapshot with
            {
                Status = RunStatuses.CANCELING,
                FailureMessage = null
            };
            return this._activeRunCts;
        }
    }

    /// <inheritdoc />
    public void SetRunContext(string runId, string runDirectory)
    {
        lock (this._sync)
        {
            this._snapshot = this._snapshot with
            {
                RunId = runId,
                RunDirectory = runDirectory
            };
        }
    }

    /// <inheritdoc />
    public void UpdateStatus(string status, DateTimeOffset? completedAtUtc, string? failureMessage)
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

    /// <inheritdoc />
    public void CompleteRun(string status, RunArtefacts artefacts, string? failureMessage)
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
    }

    /// <inheritdoc />
    public void FailRun(string status, string failureMessage)
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
    }

    /// <inheritdoc />
    public void ReleaseRun(CancellationTokenSource runCts)
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

