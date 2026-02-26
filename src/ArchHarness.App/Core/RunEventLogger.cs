using ArchHarness.App.Copilot;
using ArchHarness.App.Storage;

namespace ArchHarness.App.Core;

/// <summary>
/// Handles run event logging and Copilot session event pumping for the orchestrator.
/// </summary>
public sealed class RunEventLogger
{
    private readonly IArtefactStore _artefactStore;
    private readonly ICopilotSessionEventStream _sessionEventStream;

    /// <summary>
    /// Initializes a new instance of the <see cref="RunEventLogger"/> class.
    /// </summary>
    /// <param name="artefactStore">Store for persisting run artefacts.</param>
    /// <param name="sessionEventStream">Stream of Copilot session events.</param>
    public RunEventLogger(IArtefactStore artefactStore, ICopilotSessionEventStream sessionEventStream)
    {
        _artefactStore = artefactStore;
        _sessionEventStream = sessionEventStream;
    }

    /// <summary>
    /// Appends a structured event to the run log.
    /// </summary>
    /// <param name="runDirectory">The run artefact directory.</param>
    /// <param name="eventData">The event payload to log.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the async operation.</returns>
    public Task AppendEventAsync(string runDirectory, object eventData, CancellationToken cancellationToken)
        => _artefactStore.AppendEventAsync(runDirectory, eventData, cancellationToken);

    /// <summary>
    /// Continuously reads Copilot session events and persists them to the run log
    /// until cancellation is requested.
    /// </summary>
    /// <param name="runDirectory">The run artefact directory.</param>
    /// <param name="runId">The unique run identifier.</param>
    /// <param name="cancellationToken">Token to signal shutdown.</param>
    /// <returns>A task that completes when the pump stops.</returns>
    public async Task PumpSessionEventsAsync(string runDirectory, string runId, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in _sessionEventStream.ReadAllAsync(cancellationToken))
            {
                await _artefactStore.AppendEventAsync(runDirectory, new
                {
                    runId,
                    source = "copilot.session",
                    eventType = evt.EventType,
                    sessionId = evt.SessionId,
                    model = evt.Model,
                    details = evt.Details,
                    timestampUtc = evt.TimestampUtc
                }, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on run shutdown when stopping event pump.
        }
    }
}
