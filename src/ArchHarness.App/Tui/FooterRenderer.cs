namespace ArchHarness.App.Tui;

/// <summary>
/// Renders navigation footers, live monitor footers, and the awaiting-input status banner.
/// </summary>
public static class FooterRenderer
{
    /// <summary>
    /// Renders the navigation footer bar.
    /// </summary>
    public static void RenderFooter()
    {
        Console.WriteLine();
        int width = Math.Max(60, Console.WindowWidth - 1);
        ChatTerminalRenderer.WriteHRule(width, ConsoleColor.DarkGray);
        ChatTerminalRenderer.WriteNavKey("1", "Setup");
        ChatTerminalRenderer.WriteNavKey("2", "Monitor");
        ChatTerminalRenderer.WriteNavKey("3", "Logs");
        ChatTerminalRenderer.WriteNavKey("4", "Artefacts");
        ChatTerminalRenderer.WriteNavKey("5", "Review");
        ChatTerminalRenderer.WriteNavKey("6", "Prompts");
        ChatTerminalRenderer.WriteColored("   [", ConsoleColor.DarkGray);
        ChatTerminalRenderer.WriteColored("Q", ConsoleColor.Yellow);
        ChatTerminalRenderer.WriteColored("] Quit", ConsoleColor.DarkGray);
        Console.WriteLine();
    }

    /// <summary>
    /// Renders the live monitor footer at the specified row with available key bindings.
    /// </summary>
    /// <param name="footerRow">The console row at which to render the footer.</param>
    /// <param name="width">The available console width.</param>
    public static void RenderLiveFooter(int footerRow, int width)
    {
        Console.SetCursorPosition(0, footerRow);
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write(new string('─', width));
        Console.SetCursorPosition(0, footerRow + 1);
        Console.ResetColor();
        ChatTerminalRenderer.WriteNavKey("A", "Select Agent");
        ChatTerminalRenderer.WriteColored("   [", ConsoleColor.DarkGray);
        ChatTerminalRenderer.WriteColored("Q", ConsoleColor.Yellow);
        ChatTerminalRenderer.WriteColored("] Quit", ConsoleColor.DarkGray);
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write(new string(' ', Math.Max(0, width - 30)));
        Console.ResetColor();
    }

    /// <summary>
    /// Renders the awaiting-input banner when the agent requests user clarification.
    /// </summary>
    /// <param name="question">The question the agent is asking, if any.</param>
    public static void RenderAwaitingInputBanner(string? question)
    {
        int width = Math.Max(60, Console.WindowWidth - 1);
        int row = 0;

        WriteLineAt(row++, "", width, ConsoleColor.Gray);

        Console.SetCursorPosition(0, row);
        Console.BackgroundColor = ConsoleColor.DarkYellow;
        Console.ForegroundColor = ConsoleColor.Black;
        string banner = "  ! AWAITING INPUT  -  Agent requested clarification";
        Console.Write(banner.PadRight(width));
        Console.ResetColor();
        row++;

        WriteLineAt(row++, string.Empty, width, ConsoleColor.Gray);

        if (!string.IsNullOrWhiteSpace(question))
        {
            WriteLineAt(row++, "  Question:", width, ConsoleColor.Yellow);
            WriteLineAt(row++, $"  {question}", width, ConsoleColor.White);
        }
        else
        {
            WriteLineAt(row++, "  Question: (none provided)", width, ConsoleColor.DarkGray);
        }

        WriteLineAt(row++, string.Empty, width, ConsoleColor.Gray);
        WriteLineAt(row, "  Answer in the prompt below to continue...", width, ConsoleColor.DarkGray);
    }

    private static void WriteLineAt(int row, string text, int width, ConsoleColor color)
    {
        Console.SetCursorPosition(0, row);
        Console.ForegroundColor = color;
        string output = text.Length > width ? text[..width] : text;
        Console.Write(output.PadRight(width));
        Console.ResetColor();
    }
}
