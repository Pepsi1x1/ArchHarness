using System.Text;

namespace ArchHarness.Desktop;

/// <summary>
/// Thread-safe aggregator that accumulates agent streaming transcript content,
/// keyed by agent identifier.
/// </summary>
public sealed class AgentTranscriptAggregator
{
    private readonly Dictionary<string, StringBuilder> _transcripts = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
    private readonly object _sync = new object();

    /// <summary>
    /// Appends a content delta to the transcript for the specified agent.
    /// </summary>
    /// <param name="agentId">The agent identifier.</param>
    /// <param name="delta">The content fragment to append.</param>
    public void AppendDelta(string agentId, string delta)
    {
        lock (this._sync)
        {
            if (!this._transcripts.TryGetValue(agentId, out StringBuilder? transcript))
            {
                transcript = new StringBuilder();
                this._transcripts[agentId] = transcript;
            }

            transcript.Append(delta);
        }
    }

    /// <summary>
    /// Returns the accumulated transcript text for the specified agent, or <see langword="null"/>
    /// if no content has been recorded for that agent.
    /// </summary>
    /// <param name="agentId">The agent identifier.</param>
    /// <returns>The transcript text, or null if none exists.</returns>
    public string? GetTranscript(string agentId)
    {
        lock (this._sync)
        {
            return this._transcripts.TryGetValue(agentId, out StringBuilder? transcript)
                ? transcript.ToString()
                : null;
        }
    }

    /// <summary>
    /// Removes all accumulated transcripts.
    /// </summary>
    public void Clear()
    {
        lock (this._sync)
        {
            this._transcripts.Clear();
        }
    }
}
