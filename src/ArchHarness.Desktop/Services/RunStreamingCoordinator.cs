using ArchHarness.App.Copilot;
using ArchHarness.App.Core;

namespace ArchHarness.Desktop;

/// <summary>
/// Consumes the agent streaming and Copilot session event streams, delegating
/// transcript accumulation to <see cref="AgentTranscriptAggregator"/> and raising
/// per-event callbacks for UI-layer integration.
/// </summary>
public sealed class RunStreamingCoordinator
{
    private readonly IAgentStreamEventStream _agentStream;
    private readonly ICopilotSessionEventStream _sessionStream;
    private readonly AgentTranscriptAggregator _transcripts;

    /// <summary>
    /// Initializes a new instance of the <see cref="RunStreamingCoordinator"/> class.
    /// </summary>
    /// <param name="agentStream">The agent streaming event source.</param>
    /// <param name="sessionStream">The Copilot session lifecycle event source.</param>
    /// <param name="transcripts">The transcript aggregator for accumulating agent output.</param>
    public RunStreamingCoordinator(
        IAgentStreamEventStream agentStream,
        ICopilotSessionEventStream sessionStream,
        AgentTranscriptAggregator transcripts)
    {
        this._agentStream = agentStream;
        this._sessionStream = sessionStream;
        this._transcripts = transcripts;
    }

    /// <summary>Gets the underlying transcript aggregator.</summary>
    public AgentTranscriptAggregator Transcripts => this._transcripts;

    /// <summary>
    /// Reads agent streaming deltas, accumulates transcript content, and invokes
    /// <paramref name="onDelta"/> for each event. The caller is responsible for
    /// dispatching to the UI thread if needed.
    /// </summary>
    /// <param name="onDelta">Callback invoked for each agent delta event.</param>
    /// <param name="cancellationToken">Token to signal cancellation.</param>
    public async Task ConsumeAgentStreamAsync(Action<AgentStreamDeltaEvent> onDelta, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (AgentStreamDeltaEvent evt in this._agentStream.ReadAllAsync(cancellationToken))
            {
                this._transcripts.AppendDelta(evt.AgentId, evt.DeltaContent);
                onDelta(evt);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on run shutdown.
        }
    }

    /// <summary>
    /// Reads Copilot session lifecycle events and invokes <paramref name="onEvent"/>
    /// for each event. The caller is responsible for dispatching to the UI thread if needed.
    /// </summary>
    /// <param name="onEvent">Callback invoked for each session lifecycle event.</param>
    /// <param name="cancellationToken">Token to signal cancellation.</param>
    public async Task ConsumeSessionEventsAsync(Action<CopilotSessionLifecycleEvent> onEvent, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (CopilotSessionLifecycleEvent evt in this._sessionStream.ReadAllAsync(cancellationToken))
            {
                onEvent(evt);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on run shutdown.
        }
    }
}
