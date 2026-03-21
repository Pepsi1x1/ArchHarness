using ArchHarness.App.Copilot;
using ArchHarness.App.Storage;

namespace ArchHarness.App.Core;

/// <summary>
/// Pumps agent stream delta events from the event stream and persists them to the artefact store.
/// </summary>
public sealed class AgentStreamEventPump
{
    private readonly IAgentStreamEventStream _agentStreamEventStream;
    private readonly IArtefactStore _artefactStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentStreamEventPump"/> class.
    /// </summary>
    /// <param name="agentStreamEventStream">Stream for agent delta events.</param>
    /// <param name="artefactStore">Store for persisting run artefacts.</param>
    public AgentStreamEventPump(IAgentStreamEventStream agentStreamEventStream, IArtefactStore artefactStore)
    {
        this._agentStreamEventStream = agentStreamEventStream;
        this._artefactStore = artefactStore;
    }

    /// <summary>
    /// Reads agent delta events and appends them to the run event log until cancelled.
    /// </summary>
    /// <param name="runDirectory">The run output directory for event storage.</param>
    /// <param name="runId">The run identifier to tag each event.</param>
    /// <param name="cancellationToken">Token to signal cancellation and stop the pump.</param>
    public async Task PumpAgentEventsAsync(string runDirectory, string runId, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (AgentStreamDeltaEvent evt in this._agentStreamEventStream.ReadAllAsync(cancellationToken))
            {
                await this._artefactStore.AppendEventAsync(runDirectory, new
                {
                    runId,
                    kind = "agent-delta",
                    source = evt.AgentRole,
                    agentId = evt.AgentId,
                    agentRole = evt.AgentRole,
                    message = evt.DeltaContent,
                    contentFormat = evt.ContentFormat,
                    streamKind = evt.StreamKind,
                    title = evt.Title,
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