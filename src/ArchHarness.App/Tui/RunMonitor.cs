using ArchHarness.App.Core;

namespace ArchHarness.App.Tui;

/// <summary>
/// Handles rendering of the run monitor screens (live and complete).
/// </summary>
public static class RunMonitor
{
    /// <summary>
    /// Maps spinner characters to their corresponding phase description.
    /// </summary>
    public static readonly IReadOnlyDictionary<char, string> SPINNER_PHASE_MAP = new Dictionary<char, string>
    {
        { '|', "Analyzing" },
        { '/', "Planning" },
        { '-', "Executing" },
        { '\\', "Finalizing" }
    };

    private const int MAX_EVENT_ROWS = 16;
    private const int FOOTER_ROWS = 2;

    /// <summary>
    /// Renders the live run monitor with a spinner and recent events.
    /// </summary>
    /// <param name="events">The thread-safe list of progress events.</param>
    /// <param name="spinner">The current spinner character.</param>
    /// <param name="initialized">Tracks whether the console has been cleared for the first frame.</param>
    public static void RenderLive(List<RuntimeProgressEvent> events, char spinner, ref bool initialized)
    {
        string phase = SPINNER_PHASE_MAP.GetValueOrDefault(spinner, "Finalizing");

        List<(string Text, ConsoleColor Color)> rows = new List<(string, ConsoleColor)>
        {
            ("  RUN MONITOR  (live)", ConsoleColor.Cyan),
            ($"  Status: {spinner} {phase}...", ConsoleColor.White),
            (string.Empty, ConsoleColor.DarkGray),
            ("  Recent Agent Activity:", ConsoleColor.Cyan)
        };

        List<RuntimeProgressEvent> snapshot;
        lock (events)
        {
            snapshot = events.TakeLast(MAX_EVENT_ROWS).ToList();
        }

        foreach (RuntimeProgressEvent evt in snapshot)
        {
            ConsoleColor color = ChatTerminalRenderer.GetEventColor(evt.Message);
            rows.Add(($"  > [{evt.TimestampUtc:HH:mm:ss}] {evt.Source}: {evt.Message}", color));
            if (!string.IsNullOrWhiteSpace(evt.Prompt))
            {
                rows.Add(($"    | {ChatTerminalRenderer.SummarizePrompt(evt.Prompt)}", ConsoleColor.DarkGray));
            }
        }

        int targetRows = 4 + (MAX_EVENT_ROWS * 2);
        while (rows.Count < targetRows)
        {
            rows.Add((string.Empty, ConsoleColor.Gray));
        }

        if (!initialized)
        {
            Console.Clear();
            initialized = true;
        }

        int width = Math.Max(20, Console.WindowWidth - 1);
        int maxRenderableRows = Math.Min(rows.Count, Math.Max(1, Console.WindowHeight - 1));
        for (int i = 0; i < maxRenderableRows; i++)
        {
            Console.SetCursorPosition(0, i);
            (string text, ConsoleColor color) = rows[i];
            if (text.Length > width)
            {
                text = text[..width];
            }

            Console.ForegroundColor = color;
            Console.Write(text.PadRight(width));
        }

        Console.ResetColor();
    }

    /// <summary>
    /// Renders the live run monitor with a split layout: top half shows the event log and
    /// bottom half shows a scrollable buffer of the selected agent's stream output.
    /// </summary>
    /// <param name="events">The thread-safe list of progress events.</param>
    /// <param name="agentEvents">The thread-safe list of agent stream delta events.</param>
    /// <param name="selectedAgentId">The currently selected agent ID, or null if none selected.</param>
    /// <param name="availableAgents">The available agents to display in the selector.</param>
    /// <param name="spinnerChar">The current spinner character.</param>
    /// <param name="firstRender">Tracks whether the console has been cleared for the first frame.</param>
    public static void RenderLiveWithAgentView(
        List<RuntimeProgressEvent> events,
        List<AgentStreamDeltaEvent> agentEvents,
        string? selectedAgentId,
        IEnumerable<(string Id, string Role)> availableAgents,
        char spinnerChar,
        ref bool firstRender)
    {
        if (!firstRender)
        {
            Console.Clear();
            firstRender = true;
        }

        string phase = SPINNER_PHASE_MAP.GetValueOrDefault(spinnerChar, "Finalizing");
        int width = Math.Max(20, Console.WindowWidth - 1);
        int totalRows = Math.Max(10, Console.WindowHeight);
        int footerRow = totalRows - FOOTER_ROWS;
        int contentRows = footerRow;
        int midRow = contentRows / 2;

        // ── Top half: event log ──────────────────────────────────────────────────────
        List<(string Text, ConsoleColor Color)> topRows = new List<(string, ConsoleColor)>
        {
            ("  RUN MONITOR  (live)", ConsoleColor.Cyan),
            ($"  Status: {spinnerChar} {phase}...", ConsoleColor.White),
            (string.Empty, ConsoleColor.DarkGray),
            ("  Recent Agent Activity:", ConsoleColor.Cyan)
        };

        int maxEventRows = Math.Max(1, midRow - topRows.Count);
        List<RuntimeProgressEvent> eventsSnapshot;
        lock (events)
        {
            eventsSnapshot = events.TakeLast(maxEventRows).ToList();
        }

        foreach (RuntimeProgressEvent evt in eventsSnapshot)
        {
            ConsoleColor color = ChatTerminalRenderer.GetEventColor(evt.Message);
            topRows.Add(($"  > [{evt.TimestampUtc:HH:mm:ss}] {evt.Source}: {evt.Message}", color));
            if (!string.IsNullOrWhiteSpace(evt.Prompt))
            {
                topRows.Add(($"    | {ChatTerminalRenderer.SummarizePrompt(evt.Prompt)}", ConsoleColor.DarkGray));
            }
        }

        while (topRows.Count < midRow)
        {
            topRows.Add((string.Empty, ConsoleColor.Gray));
        }

        for (int i = 0; i < midRow && i < topRows.Count; i++)
        {
            Console.SetCursorPosition(0, i);
            (string text, ConsoleColor color) = topRows[i];
            if (text.Length > width)
            {
                text = text[..width];
            }

            Console.ForegroundColor = color;
            Console.Write(text.PadRight(width));
        }

        // ── Separator ────────────────────────────────────────────────────────────────
        Console.SetCursorPosition(0, midRow);
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write(new string('─', width));

        // ── Agent stream pane ─────────────────────────────────────────────────────────
        List<(string Id, string Role)> agentList = availableAgents.ToList();
        (int agentSelectorRow, int outputStartRow, int outputRows) = AgentStreamPane.CalculateLayout(midRow, contentRows);
        AgentStreamPane.RenderAgentSelector(agentSelectorRow, width, selectedAgentId, agentList);
        AgentStreamPane.RenderAgentOutput(outputStartRow, outputRows, width, selectedAgentId, agentEvents, agentList);

        // ── Footer ────────────────────────────────────────────────────────────────────
        if (footerRow < totalRows)
        {
            FooterRenderer.RenderLiveFooter(footerRow, width);
        }

        Console.ResetColor();
    }

    /// <summary>
    /// Renders the completed run monitor screen with timeline and artefact details.
    /// </summary>
    /// <param name="artefacts">The run artefacts containing run ID and directory.</param>
    /// <param name="events">The thread-safe list of progress events.</param>
    public static void RenderComplete(RunArtefacts artefacts, List<RuntimeProgressEvent> events)
    {
        Console.Clear();
        int width = Math.Max(60, Console.WindowWidth - 1);

        ChatTerminalRenderer.WriteScreenTitle("Run Monitor", width);
        ChatTerminalRenderer.WriteLabel("Status    ", "Completed", ConsoleColor.Cyan);
        ChatTerminalRenderer.WriteLabel("Run ID    ", artefacts.RunId);
        ChatTerminalRenderer.WriteLabel("Directory ", artefacts.RunDirectory);
        Console.WriteLine();
        ChatTerminalRenderer.WriteColored("  Timeline:", ConsoleColor.Cyan);
        Console.WriteLine();

        List<RuntimeProgressEvent> snapshot;
        lock (events)
        {
            snapshot = events.ToList();
        }

        foreach (RuntimeProgressEvent evt in snapshot.TakeLast(24))
        {
            ConsoleColor color = ChatTerminalRenderer.GetEventColor(evt.Message);
            ChatTerminalRenderer.WriteColored("  > ", ConsoleColor.DarkGray);
            ChatTerminalRenderer.WriteColored($"[{evt.TimestampUtc:HH:mm:ss}] ", ConsoleColor.Cyan);
            ChatTerminalRenderer.WriteColored($"{evt.Source}: ", ConsoleColor.White);
            ChatTerminalRenderer.WriteColored(evt.Message, color);
            Console.WriteLine();
            if (!string.IsNullOrWhiteSpace(evt.Prompt))
            {
                ChatTerminalRenderer.WriteMuted($"    | {ChatTerminalRenderer.SummarizePrompt(evt.Prompt)}");
            }
        }
    }
}
