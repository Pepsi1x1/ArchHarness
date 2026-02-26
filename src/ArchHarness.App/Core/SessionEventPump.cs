using ArchHarness.App.Copilot;
using ArchHarness.App.Storage;

namespace ArchHarness.App.Core;

/// <summary>
/// Pumps Copilot session lifecycle events from the event stream and persists them to the artefact store.
/// </summary>
public sealed class SessionEventPump
{
    private readonly ICopilotSessionEventStream _sessionEventStream;
    private readonly IArtefactStore _artefactStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionEventPump"/> class.
    /// </summary>
    /// <param name="sessionEventStream">Stream for Copilot session lifecycle events.</param>
    /// <param name="artefactStore">Store for persisting run artefacts.</param>
    public SessionEventPump(ICopilotSessionEventStream sessionEventStream, IArtefactStore artefactStore)
    {
        this._sessionEventStream = sessionEventStream;
        this._artefactStore = artefactStore;
    }

    /// <summary>
    /// Reads session lifecycle events and appends them to the run event log until cancelled.
    /// </summary>
    /// <param name="runDirectory">The run output directory for event storage.</param>
    /// <param name="runId">The run identifier to tag each event.</param>
    /// <param name="cancellationToken">Token to signal cancellation and stop the pump.</param>
    public async Task PumpSessionEventsAsync(string runDirectory, string runId, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (CopilotSessionLifecycleEvent evt in this._sessionEventStream.ReadAllAsync(cancellationToken))
            {
                await this._artefactStore.AppendEventAsync(runDirectory, new
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
