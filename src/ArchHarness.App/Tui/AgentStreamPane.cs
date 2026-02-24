using ArchHarness.App.Core;

namespace ArchHarness.App.Tui;

/// <summary>
/// Renders the agent stream pane within the live run monitor,
/// including the agent selector row and scrollable delta content buffer.
/// </summary>
public static class AgentStreamPane
{
    /// <summary>
    /// Calculates the layout dimensions for the agent stream pane.
    /// </summary>
    /// <param name="midRow">The row at which the separator appears.</param>
    /// <param name="contentRows">The total content rows available before the footer.</param>
    /// <returns>The agent selector row, output start row, and output row count.</returns>
    public static (int AgentSelectorRow, int OutputStartRow, int OutputRows) CalculateLayout(int midRow, int contentRows)
    {
        int agentSelectorRow = midRow + 1;
        int outputStartRow = agentSelectorRow + 1;
        int outputRows = Math.Max(0, contentRows - outputStartRow);
        return (agentSelectorRow, outputStartRow, outputRows);
    }

    /// <summary>
    /// Renders the agent selector row at the specified console row.
    /// </summary>
    /// <param name="row">The console row at which to render the selector.</param>
    /// <param name="width">The available console width.</param>
    /// <param name="selectedAgentId">The currently selected agent ID, or null if none selected.</param>
    /// <param name="agentList">The available agents to display.</param>
    public static void RenderAgentSelector(
        int row,
        int width,
        string? selectedAgentId,
        List<(string Id, string Role)> agentList)
    {
        Console.SetCursorPosition(0, row);
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write(new string(' ', width));
        Console.SetCursorPosition(0, row);
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("  Agents: ");

        foreach ((string agentId, string agentRole) in agentList)
        {
            bool isSelected = agentId == selectedAgentId;
            if (isSelected)
            {
                Console.BackgroundColor = ConsoleColor.DarkCyan;
                Console.ForegroundColor = ConsoleColor.Black;
            }
            else
            {
                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.White;
            }

            Console.Write($" {agentRole} ");
            Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(" ");
        }

        Console.ResetColor();
    }

    /// <summary>
    /// Renders the scrollable agent output buffer in the bottom half of the split layout.
    /// </summary>
    /// <param name="startRow">The first console row of the output area.</param>
    /// <param name="outputRows">The number of rows available for output.</param>
    /// <param name="width">The available console width.</param>
    /// <param name="selectedAgentId">The currently selected agent ID, or null if none selected.</param>
    /// <param name="agentEvents">The thread-safe list of agent stream delta events.</param>
    /// <param name="agentList">The available agents.</param>
    public static void RenderAgentOutput(
        int startRow,
        int outputRows,
        int width,
        string? selectedAgentId,
        List<AgentStreamDeltaEvent> agentEvents,
        List<(string Id, string Role)> agentList)
    {
        if (outputRows <= 0)
        {
            return;
        }

        if (string.IsNullOrEmpty(selectedAgentId) || agentList.Count == 0)
        {
            RenderNoAgentPrompt(startRow, outputRows, width);
            return;
        }

        RenderSelectedAgentContent(startRow, outputRows, width, selectedAgentId, agentEvents);
    }

    private static void RenderNoAgentPrompt(int startRow, int outputRows, int width)
    {
        for (int i = startRow; i < startRow + outputRows; i++)
        {
            Console.SetCursorPosition(0, i);
            Console.Write(new string(' ', width));
        }

        int promptRow = startRow + (outputRows / 2);
        if (promptRow < startRow + outputRows)
        {
            Console.SetCursorPosition(0, promptRow);
            string promptText = "  Press [A] to select an agent to view output";
            if (promptText.Length > width)
            {
                promptText = promptText[..width];
            }

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(promptText.PadRight(width));
        }
    }

    private static void RenderSelectedAgentContent(
        int startRow,
        int outputRows,
        int width,
        string selectedAgentId,
        List<AgentStreamDeltaEvent> agentEvents)
    {
        List<AgentStreamDeltaEvent> agentSnapshot;
        lock (agentEvents)
        {
            agentSnapshot = agentEvents.Where(e => e.AgentId == selectedAgentId).ToList();
        }

        IReadOnlyList<string> formattedLines = AgentOutputFormatter.FormatDeltaContent(agentSnapshot, width);
        IReadOnlyList<string> visibleLines = formattedLines.TakeLast(outputRows).ToArray();

        for (int i = 0; i < outputRows; i++)
        {
            Console.SetCursorPosition(0, startRow + i);
            string line = i < visibleLines.Count ? visibleLines[i] : string.Empty;

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(line.PadRight(width));
        }
    }
}
