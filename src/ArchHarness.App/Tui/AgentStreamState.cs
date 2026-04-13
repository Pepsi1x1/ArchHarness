using ArchHarness.App.Copilot;
using ArchHarness.App.Core;

namespace ArchHarness.App.Tui;

/// <summary>
/// Defines the contract for accumulating agent stream delta events into a shared buffer.
/// </summary>
internal interface IAgentStreamAccumulator
{
    /// <summary>
    /// Appends a single agent stream delta event to the shared buffer.
    /// </summary>
    /// <param name="evt">The event to accumulate.</param>
    void Accumulate(AgentStreamDeltaEvent evt);
}

/// <summary>
/// Thread-safe accumulator that locks and appends events to a shared list,
/// maintaining a per-agent index for fast filtering.
/// </summary>
internal sealed class AgentStreamAccumulator : IAgentStreamAccumulator
{
    private readonly List<AgentStreamDeltaEvent> _events;
    private readonly Dictionary<string, List<AgentStreamDeltaEvent>> _byAgent = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentStreamAccumulator"/> class.
    /// </summary>
    /// <param name="events">The shared list to append events to.</param>
    internal AgentStreamAccumulator(List<AgentStreamDeltaEvent> events)
    {
        this._events = events;
    }

    /// <inheritdoc />
    public void Accumulate(AgentStreamDeltaEvent evt)
    {
        lock (this._events)
        {
            this._events.Add(evt);
            if (!this._byAgent.TryGetValue(evt.AgentId, out List<AgentStreamDeltaEvent>? agentList))
            {
                agentList = new List<AgentStreamDeltaEvent>();
                this._byAgent[evt.AgentId] = agentList;
            }

            agentList.Add(evt);
        }
    }

    /// <summary>
    /// Returns a snapshot of events for the given agent, or an empty list if none exist.
    /// </summary>
    internal List<AgentStreamDeltaEvent> SnapshotForAgent(string agentId)
    {
        lock (this._events)
        {
            return this._byAgent.TryGetValue(agentId, out List<AgentStreamDeltaEvent>? agentList)
                ? agentList.ToList()
                : new List<AgentStreamDeltaEvent>();
        }
    }

    /// <summary>
    /// Returns the list of distinct agents that have produced stream events.
    /// </summary>
    internal List<(string Id, string Role)> GetAvailableAgents()
    {
        lock (this._events)
        {
            return this._byAgent
                .Select(kvp => (kvp.Key, kvp.Value[0].AgentRole))
                .OrderBy(a => a.Key)
                .ToList();
        }
    }

    /// <summary>
    /// Returns the sorted list of distinct agent IDs.
    /// </summary>
    internal List<string> GetAgentIds()
    {
        lock (this._events)
        {
            List<string> ids = new List<string>(this._byAgent.Keys);
            ids.Sort(StringComparer.Ordinal);
            return ids;
        }
    }
}

/// <summary>
/// Owns the agent stream consumption state, including the event buffer,
/// selected agent tracking, and background stream consumption.
/// </summary>
public sealed class AgentStreamState
{
    private readonly IAgentStreamEventStream _eventStream;
    private readonly List<AgentStreamDeltaEvent> _events = new List<AgentStreamDeltaEvent>();
    private readonly IAgentStreamAccumulator _accumulator;
    private string? _selectedAgentId;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentStreamState"/> class.
    /// </summary>
    /// <param name="eventStream">The agent stream event stream to consume from.</param>
    public AgentStreamState(IAgentStreamEventStream eventStream)
    {
        this._eventStream = eventStream;
        this._accumulator = new AgentStreamAccumulator(this._events);
    }

    /// <summary>
    /// Gets the thread-safe list of consumed agent stream delta events.
    /// </summary>
    public List<AgentStreamDeltaEvent> Events => this._events;

    /// <summary>
    /// Gets the currently selected agent ID, or null if none is selected.
    /// </summary>
    public string? SelectedAgentId => this._selectedAgentId;

    /// <summary>
    /// Returns the list of distinct agents that have produced stream events.
    /// </summary>
    /// <returns>A list of agent ID and role pairs, ordered by agent ID.</returns>
    public List<(string Id, string Role)> GetAvailableAgents()
    {
        return ((AgentStreamAccumulator)this._accumulator).GetAvailableAgents();
    }

    /// <summary>
    /// Returns a snapshot of events for the given agent.
    /// </summary>
    public List<AgentStreamDeltaEvent> SnapshotForAgent(string agentId)
    {
        return ((AgentStreamAccumulator)this._accumulator).SnapshotForAgent(agentId);
    }

    /// <summary>
    /// Cycles the selected agent to the next available agent in the list.
    /// </summary>
    public void CycleSelectedAgent()
    {
        List<string> agentIds = ((AgentStreamAccumulator)this._accumulator).GetAgentIds();

        if (agentIds.Count > 0)
        {
            int currentIndex = this._selectedAgentId == null ? -1 : agentIds.IndexOf(this._selectedAgentId);
            this._selectedAgentId = agentIds[(currentIndex + 1) % agentIds.Count];
        }
    }

    /// <summary>
    /// Consumes events from the agent stream until cancellation is requested.
    /// </summary>
    /// <param name="cancellationToken">Token to signal cancellation.</param>
    public async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (AgentStreamDeltaEvent evt in this._eventStream.ReadAllAsync(cancellationToken))
            {
                this._accumulator.Accumulate(evt);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on run shutdown when stopping the agent stream pump.
        }
    }
}
