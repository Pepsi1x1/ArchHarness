using ArchHarness.App.Core;

namespace ArchHarness.App.Tui;

/// <summary>
/// Renders the chat setup confirmation screen with boxed panels and styled chat messages.
/// </summary>
public static class SetupScreenRenderer
{
    /// <summary>
    /// Renders the setup/chat preview screen with run configuration details.
    /// </summary>
    /// <param name="request">The run request containing task and workspace information.</param>
    /// <param name="setupSummary">The agent summary of the setup conversation.</param>
    public static void RenderSetupScreen(RunRequest request, string setupSummary)
    {
        Console.Clear();
        int width = Math.Max(60, Console.WindowWidth - 1);
        int panelWidth = Math.Max(56, Math.Min(width - 4, 110));

        ChatTerminalRenderer.WriteScreenTitle("Chat / Setup", width);
        WriteBoxedSetupPanel(
            panelWidth,
            [
                ("Task", request.TaskPrompt),
                ("Workspace", request.WorkspacePath),
                ("Mode", request.WorkspaceMode),
                ("Workflow", "auto (orchestrator-driven)"),
                ("Build", request.BuildCommand ?? "(none)")
            ]);

        Console.WriteLine();
        ChatTerminalRenderer.WriteColored("  Chat Preview", ConsoleColor.Cyan);
        Console.WriteLine();
        ChatTerminalRenderer.WriteColored($"  {new string('─', Math.Max(18, Math.Min(panelWidth - 4, width - 6)))}", ConsoleColor.DarkGray);
        Console.WriteLine();
        WriteStyledChatMessage("▶ You:", request.TaskPrompt, ConsoleColor.Cyan, width);
        WriteStyledChatMessage("◈ Agent:", setupSummary, ConsoleColor.White, width);
        Console.WriteLine();
        ChatTerminalRenderer.WriteColored("  Press any key to start the run ", ConsoleColor.Yellow);
        ChatTerminalRenderer.WriteColored("▸", ConsoleColor.Yellow);
        Console.WriteLine();
    }

    /// <summary>
    /// Writes a boxed setup configuration panel with labeled rows.
    /// </summary>
    /// <param name="panelWidth">The total panel width including borders.</param>
    /// <param name="rows">The label-value pairs to display.</param>
    public static void WriteBoxedSetupPanel(int panelWidth, IEnumerable<(string Label, string Value)> rows)
    {
        WriteBoxedSetupPanel(panelWidth, rows, focusedIndex: -1);
    }

    /// <summary>
    /// Writes a boxed setup configuration panel with labeled rows and an optional focused row.
    /// </summary>
    /// <param name="panelWidth">The total panel width including borders.</param>
    /// <param name="rows">The label-value pairs to display.</param>
    /// <param name="focusedIndex">Zero-based index of the currently focused row, or -1 for no focus.</param>
    public static void WriteBoxedSetupPanel(int panelWidth, IEnumerable<(string Label, string Value)> rows, int focusedIndex)
    {
        int contentWidth = Math.Max(24, panelWidth - 2);

        // ╔═══╗ top border
        ChatTerminalRenderer.WriteColored("  ╔", ConsoleColor.Cyan);
        ChatTerminalRenderer.WriteColored(new string('═', contentWidth), ConsoleColor.Cyan);
        ChatTerminalRenderer.WriteColored("╗", ConsoleColor.Cyan);
        Console.WriteLine();

        // "ARCH HARNESS" title in bright Cyan
        string archTitle = "  ARCH HARNESS";
        ChatTerminalRenderer.WriteColored("  ║", ConsoleColor.Cyan);
        ChatTerminalRenderer.WriteColored(archTitle.PadRight(contentWidth), ConsoleColor.Cyan);
        ChatTerminalRenderer.WriteColored("║", ConsoleColor.Cyan);
        Console.WriteLine();

        // "◈ CHAT SETUP ◈" subtitle with DarkCyan flanking decorators
        string subTitle = "  ◈ CHAT SETUP ◈";
        ChatTerminalRenderer.WriteColored("  ║", ConsoleColor.Cyan);
        ChatTerminalRenderer.WriteColored(subTitle.PadRight(contentWidth), ConsoleColor.DarkCyan);
        ChatTerminalRenderer.WriteColored("║", ConsoleColor.Cyan);
        Console.WriteLine();

        // ╠═══╣ separator between title header and fields
        ChatTerminalRenderer.WriteColored("  ╠", ConsoleColor.Cyan);
        ChatTerminalRenderer.WriteColored(new string('═', contentWidth), ConsoleColor.Cyan);
        ChatTerminalRenderer.WriteColored("╣", ConsoleColor.Cyan);
        Console.WriteLine();

        List<(string Label, string Value)> rowList = rows.ToList();
        for (int i = 0; i < rowList.Count; i++)
        {
            (string label, string value) = rowList[i];
            WriteBoxedSetupRow(contentWidth, label, value, isSelected: i == focusedIndex);
        }

        // ╠═══╣ separator before keyboard hint footer
        ChatTerminalRenderer.WriteColored("  ╠", ConsoleColor.Cyan);
        ChatTerminalRenderer.WriteColored(new string('═', contentWidth), ConsoleColor.Cyan);
        ChatTerminalRenderer.WriteColored("╣", ConsoleColor.Cyan);
        Console.WriteLine();

        // Keyboard hint footer bar
        string hint = " [↑↓] Navigate  [Enter] Edit  [←→] Cycle  [F5] Run  [Esc] Cancel";
        string paddedHint = hint.Length <= contentWidth
            ? hint.PadRight(contentWidth)
            : hint[..contentWidth];
        ChatTerminalRenderer.WriteColored("  ║", ConsoleColor.Cyan);
        ChatTerminalRenderer.WriteColored(paddedHint, ConsoleColor.DarkCyan);
        ChatTerminalRenderer.WriteColored("║", ConsoleColor.Cyan);
        Console.WriteLine();

        // ╚═══╝ bottom border
        ChatTerminalRenderer.WriteColored("  ╚", ConsoleColor.Cyan);
        ChatTerminalRenderer.WriteColored(new string('═', contentWidth), ConsoleColor.Cyan);
        ChatTerminalRenderer.WriteColored("╝", ConsoleColor.Cyan);
        Console.WriteLine();
    }

    /// <summary>
    /// Writes a single row inside the boxed setup panel.
    /// </summary>
    /// <param name="contentWidth">The available content width inside the box borders.</param>
    /// <param name="label">The row label.</param>
    /// <param name="value">The row value.</param>
    public static void WriteBoxedSetupRow(int contentWidth, string label, string value)
    {
        WriteBoxedSetupRow(contentWidth, label, value, isSelected: false);
    }

    /// <summary>
    /// Writes a single row inside the boxed setup panel with optional selection highlight.
    /// </summary>
    /// <param name="contentWidth">The available content width inside the box borders.</param>
    /// <param name="label">The row label.</param>
    /// <param name="value">The row value.</param>
    /// <param name="isSelected">Whether this row is currently focused/selected.</param>
    public static void WriteBoxedSetupRow(int contentWidth, string label, string value, bool isSelected)
    {
        string normalizedValue = string.IsNullOrWhiteSpace(value) ? "(none)" : value;
        bool hasValue = !string.IsNullOrWhiteSpace(value);
        ConsoleColor valueColor = hasValue ? ConsoleColor.DarkMagenta : ConsoleColor.DarkGray;

        string icon = GetFieldIcon(label);
        string selectionMarker = isSelected ? "►" : " ";
        string labelText = $" {selectionMarker} {icon} {label.PadRight(10)} ";
        int availableValueWidth = Math.Max(8, contentWidth - labelText.Length);
        if (normalizedValue.Length > availableValueWidth)
        {
            normalizedValue = normalizedValue[..Math.Max(0, availableValueWidth - 1)] + "…";
        }

        ChatTerminalRenderer.WriteColored("  ║", ConsoleColor.Cyan);
        if (isSelected)
        {
            ChatTerminalRenderer.WriteColored(labelText, ConsoleColor.Yellow);
            ChatTerminalRenderer.WriteColored(normalizedValue.PadRight(availableValueWidth), ConsoleColor.Yellow);
        }
        else
        {
            ChatTerminalRenderer.WriteColored(labelText, ConsoleColor.DarkGray);
            ChatTerminalRenderer.WriteColored(normalizedValue.PadRight(availableValueWidth), valueColor);
        }

        ChatTerminalRenderer.WriteColored("║", ConsoleColor.Cyan);
        Console.WriteLine();
    }

    /// <summary>
    /// Writes a styled chat message with word-wrapping and prefix alignment.
    /// </summary>
    /// <param name="prefix">The message prefix (e.g. "▶ You:").</param>
    /// <param name="message">The message body text.</param>
    /// <param name="prefixColor">The foreground color for the prefix.</param>
    /// <param name="width">The available console width.</param>
    public static void WriteStyledChatMessage(string prefix, string message, ConsoleColor prefixColor, int width)
    {
        string text = string.IsNullOrWhiteSpace(message) ? "(none)" : message.Replace(Environment.NewLine, " ");
        int contentWidth = Math.Max(20, width - 12);
        string remaining = text;
        bool firstLine = true;

        while (remaining.Length > 0)
        {
            int take = Math.Min(contentWidth, remaining.Length);
            string chunk = remaining[..take];
            if (take < remaining.Length)
            {
                int split = chunk.LastIndexOf(' ');
                if (split > 16)
                {
                    chunk = chunk[..split];
                    take = split;
                }
            }

            ChatTerminalRenderer.WriteColored("  ", ConsoleColor.DarkGray);
            if (firstLine)
            {
                ChatTerminalRenderer.WriteColored(prefix, prefixColor);
            }
            else
            {
                ChatTerminalRenderer.WriteColored(new string(' ', prefix.Length), ConsoleColor.DarkGray);
            }

            ChatTerminalRenderer.WriteColored(" ", ConsoleColor.DarkGray);
            ChatTerminalRenderer.WriteColored(chunk.Trim(), ConsoleColor.White);
            Console.WriteLine();

            remaining = remaining[take..].TrimStart();
            firstLine = false;
        }
    }

    private static string GetFieldIcon(string label)
    {
        return label.ToUpperInvariant() switch
        {
            "WORKSPACE" => "⊞",
            "MODE"      => "◎",
            "WORKFLOW"  => "◎",
            "BUILD"     => "⚡",
            _           => "▸",
        };
    }
}
