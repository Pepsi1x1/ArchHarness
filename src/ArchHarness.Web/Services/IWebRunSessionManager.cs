using ArchHarness.App.Core;
using ArchHarness.App.Storage;

namespace ArchHarness.Web.Services;

/// <summary>
/// Manages the single local run session exposed by the web host.
/// </summary>
public interface IWebRunSessionManager
{
    /// <summary>
    /// Starts a new background run.
    /// </summary>
    /// <param name="request">The run request to execute.</param>
    /// <param name="cancellationToken">Token to cancel startup before the run begins.</param>
    /// <returns>The initial snapshot after the run is accepted.</returns>
    Task<WebRunSnapshot> StartRunAsync(RunRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Resumes a previously interrupted run.
    /// </summary>
    /// <param name="runState">The persisted checkpoint to resume.</param>
    /// <param name="cancellationToken">Token to cancel startup before the resume begins.</param>
    /// <returns>The initial snapshot after resume is accepted.</returns>
    Task<WebRunSnapshot> ResumeRunAsync(PersistedRunState runState, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the current run-session snapshot.
    /// </summary>
    /// <returns>The current run state.</returns>
    WebRunSnapshot GetSnapshot();

    /// <summary>
    /// Requests cancellation of the active run, if one is running.
    /// </summary>
    /// <returns>The updated run snapshot.</returns>
    Task<WebRunSnapshot> CancelRunAsync();

    /// <summary>
    /// Streams buffered and future run events to a subscriber.
    /// </summary>
    /// <param name="cancellationToken">Token that ends the subscription.</param>
    /// <returns>An async stream of web run events.</returns>
    IAsyncEnumerable<WebRunEvent> ReadEventsAsync(CancellationToken cancellationToken);
}