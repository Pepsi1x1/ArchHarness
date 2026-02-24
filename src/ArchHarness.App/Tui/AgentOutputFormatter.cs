using ArchHarness.App.Core;

namespace ArchHarness.App.Tui;

/// <summary>
/// Aggregates and formats agent stream delta content into display-ready lines.
/// </summary>
internal static class AgentOutputFormatter
{
    /// <summary>
    /// Concatenates delta content from agent events and splits into lines capped at the given width.
    /// </summary>
    /// <param name="events">The agent stream delta events to format.</param>
    /// <param name="maxWidth">The maximum character width per line.</param>
    /// <returns>A list of formatted lines ready for rendering.</returns>
    internal static IReadOnlyList<string> FormatDeltaContent(IEnumerable<AgentStreamDeltaEvent> events, int maxWidth)
    {
        string fullContent = string.Concat(events.Select(e => e.DeltaContent));
        string[] contentLines = fullContent.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

        List<string> formatted = new List<string>(contentLines.Length);
        foreach (string rawLine in contentLines)
        {
            string line = $"  {rawLine}";
            if (line.Length > maxWidth)
            {
                line = line[..maxWidth];
            }

            formatted.Add(line);
        }

        return formatted;
    }
}
