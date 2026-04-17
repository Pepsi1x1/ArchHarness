using ArchHarness.App.Copilot;
using ArchHarness.App.Storage;

namespace ArchHarness.App.Core;

/// <summary>
/// Handles run event logging and Copilot session event pumping for the orchestrator.
/// Delegates the pump to <see cref="SessionEventPump"/> to avoid duplicating that logic.
/// </summary>
public sealed class RunEventLogger : IRunEventLogger
{
    private readonly IArtefactStore _artefactStore;
    private readonly SessionEventPump _sessionEventPump;
    private readonly AgentStreamEventPump _agentStreamEventPump;

    /// <summary>
    /// Initializes a new instance of the <see cref="RunEventLogger"/> class.
    /// </summary>
    /// <param name="artefactStore">Store for persisting run artefacts.</param>
    /// <param name="sessionEventStream">Stream of Copilot session events.</param>
    public RunEventLogger(IArtefactStore artefactStore, ICopilotSessionEventStream sessionEventStream, ICopilotSdkEventStream sdkEventStream, IAgentStreamEventStream agentStreamEventStream)
    {
        this._artefactStore = artefactStore;
        this._sessionEventPump = new SessionEventPump(sessionEventStream, sdkEventStream, artefactStore);
        this._agentStreamEventPump = new AgentStreamEventPump(agentStreamEventStream, artefactStore);
    }

    /// <summary>
    /// Appends a structured event to the run log.
    /// </summary>
    /// <param name="runDirectory">The run artefact directory.</param>
    /// <param name="eventData">The event payload to log.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the async operation.</returns>
    public Task AppendEventAsync(string runDirectory, object eventData, CancellationToken cancellationToken)
        => this._artefactStore.AppendEventAsync(runDirectory, eventData, cancellationToken);

    /// <summary>
    /// Continuously reads Copilot session events and persists them to the run log
    /// until cancellation is requested. Delegates to <see cref="SessionEventPump"/>.
    /// </summary>
    /// <param name="runDirectory">The run artefact directory.</param>
    /// <param name="runId">The unique run identifier.</param>
    /// <param name="cancellationToken">Token to signal shutdown.</param>
    /// <returns>A task that completes when the pump stops.</returns>
    public Task PumpSessionEventsAsync(string runDirectory, string runId, CancellationToken cancellationToken)
        => this._sessionEventPump.PumpSessionEventsAsync(runDirectory, runId, cancellationToken);

    /// <summary>
    /// Continuously reads agent stream delta events and persists them to the run log
    /// until cancellation is requested. Delegates to <see cref="AgentStreamEventPump"/>.
    /// </summary>
    /// <param name="runDirectory">The run artefact directory.</param>
    /// <param name="runId">The unique run identifier.</param>
    /// <param name="cancellationToken">Token to signal shutdown.</param>
    /// <returns>A task that completes when the pump stops.</returns>
    public Task PumpAgentEventsAsync(string runDirectory, string runId, CancellationToken cancellationToken)
        => this._agentStreamEventPump.PumpAgentEventsAsync(runDirectory, runId, cancellationToken);

    /// <summary>
    /// Flushes all JSONL writers owned by the underlying <see cref="IArtefactStore"/> for
    /// this run, draining any pending events queued in the channel-backed writers.
    /// </summary>
    public Task CompleteRunAsync(string runDirectory, CancellationToken cancellationToken)
        => this._artefactStore.CompleteRunAsync(runDirectory, cancellationToken);
}
