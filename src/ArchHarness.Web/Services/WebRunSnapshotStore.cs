using ArchHarness.App.Core;
namespace ArchHarness.Web.Services;

/// <summary>
/// Owns mutable run-session snapshot state for the local web host.
/// </summary>
public sealed record WebRunSessionStart(
    CancellationToken ShutdownToken,
    string Status,
    DateTimeOffset StartedAt,
    string? RunId,
    string? RunDirectory,
    string? TaskPrompt,
    string? WorkspacePath,
    string? FailureMessage);

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
    CancellationTokenSource BeginRunSession(WebRunSessionStart start);

    /// <summary>
    /// Marks the active run as canceling and returns its cancellation source, if any.
    /// </summary>
    CancellationTokenSource? RequestCancellation();

    /// <summary>
    /// Marks the active run as pausing and returns its cancellation source, if it can be resumed.
    /// </summary>
    CancellationTokenSource? RequestPause();

    /// <summary>
    /// Gets a value indicating whether the active cancellation request is a pause.
    /// </summary>
    bool IsPauseRequested();

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
    /// Marks the run as paused.
    /// </summary>
    void PauseRun();

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
    private readonly object _sync = new object();
    private CancellationTokenSource? _activeRunCts;
    private bool _pauseRequested;
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
    public CancellationTokenSource BeginRunSession(WebRunSessionStart start)
    {
        lock (this._sync)
        {
            if (this._snapshot.IsRunning)
            {
                throw new InvalidOperationException("A run is already active in the local web host.");
            }

            CancellationTokenSource runCts = CancellationTokenSource.CreateLinkedTokenSource(start.ShutdownToken);
            this._activeRunCts = runCts;
            this._pauseRequested = false;
            this._snapshot = new WebRunSnapshot(
                true,
                start.Status,
                start.StartedAt,
                null,
                start.RunId,
                start.RunDirectory,
                start.TaskPrompt,
                start.WorkspacePath,
                start.FailureMessage);
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

            this._pauseRequested = false;
            this._snapshot = this._snapshot with
            {
                Status = RunStatuses.CANCELING,
                FailureMessage = null
            };
            return this._activeRunCts;
        }
    }

    /// <inheritdoc />
    public CancellationTokenSource? RequestPause()
    {
        lock (this._sync)
        {
            if (this._activeRunCts is null
                || !this._snapshot.IsRunning
                || string.IsNullOrWhiteSpace(this._snapshot.RunId)
                || string.IsNullOrWhiteSpace(this._snapshot.RunDirectory))
            {
                return null;
            }

            this._pauseRequested = true;
            this._snapshot = this._snapshot with
            {
                Status = RunStatuses.PAUSING,
                FailureMessage = null
            };
            return this._activeRunCts;
        }
    }

    /// <inheritdoc />
    public bool IsPauseRequested()
    {
        lock (this._sync)
        {
            return this._pauseRequested;
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
            if (!ShouldApplyStatusUpdate(this._snapshot, status))
            {
                return;
            }

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
            this._pauseRequested = false;
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
    public void PauseRun()
    {
        DateTimeOffset completedAt = DateTimeOffset.UtcNow;
        lock (this._sync)
        {
            this._pauseRequested = false;
            this._snapshot = this._snapshot with
            {
                IsRunning = false,
                Status = RunStatuses.PAUSED,
                CompletedAtUtc = completedAt,
                FailureMessage = null
            };
        }
    }

    /// <inheritdoc />
    public void FailRun(string status, string failureMessage)
    {
        DateTimeOffset completedAt = DateTimeOffset.UtcNow;
        lock (this._sync)
        {
            this._pauseRequested = false;
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

            this._pauseRequested = false;
        }

        runCts.Dispose();
    }

    private static bool ShouldApplyStatusUpdate(WebRunSnapshot snapshot, string nextStatus)
    {
        if (!snapshot.IsRunning)
        {
            return false;
        }

        if (!string.Equals(nextStatus, RunStatuses.RUNNING, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(snapshot.Status, RunStatuses.STARTING, StringComparison.OrdinalIgnoreCase)
            || string.Equals(snapshot.Status, RunStatuses.RESUMING, StringComparison.OrdinalIgnoreCase)
            || string.Equals(snapshot.Status, RunStatuses.RUNNING, StringComparison.OrdinalIgnoreCase);
    }
}

