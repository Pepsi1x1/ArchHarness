namespace ArchHarness.App.Core;

/// <summary>
/// Handles run event logging and Copilot session event pumping.
/// </summary>
public interface IRunEventLogger
{
    /// <summary>
    /// Appends a structured event to the run log.
    /// </summary>
    /// <param name="runDirectory">The run artefact directory.</param>
    /// <param name="eventData">The event payload to log.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the async operation.</returns>
    Task AppendEventAsync(string runDirectory, object eventData, CancellationToken cancellationToken);

    /// <summary>
    /// Continuously reads Copilot session events and persists them to the run log
    /// until cancellation is requested.
    /// </summary>
    /// <param name="runDirectory">The run artefact directory.</param>
    /// <param name="runId">The unique run identifier.</param>
    /// <param name="cancellationToken">Token to signal shutdown.</param>
    /// <returns>A task that completes when the pump stops.</returns>
    Task PumpSessionEventsAsync(string runDirectory, string runId, CancellationToken cancellationToken);

    /// <summary>
    /// Continuously reads agent stream delta events and persists them to the run log
    /// until cancellation is requested.
    /// </summary>
    /// <param name="runDirectory">The run artefact directory.</param>
    /// <param name="runId">The unique run identifier.</param>
    /// <param name="cancellationToken">Token to signal shutdown.</param>
    /// <returns>A task that completes when the pump stops.</returns>
    Task PumpAgentEventsAsync(string runDirectory, string runId, CancellationToken cancellationToken);
}
