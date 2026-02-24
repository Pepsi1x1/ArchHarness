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
        Console.Clear();
        int width = Math.Max(60, Console.WindowWidth - 1);

        Console.BackgroundColor = ConsoleColor.DarkYellow;
        Console.ForegroundColor = ConsoleColor.Black;
        string banner = "  ! AWAITING INPUT  —  Agent requested clarification";
        Console.WriteLine(banner + new string(' ', Math.Max(0, width - banner.Length)));
        Console.ResetColor();
        Console.WriteLine();

        if (!string.IsNullOrWhiteSpace(question))
        {
            ChatTerminalRenderer.WriteColored("  Question:", ConsoleColor.Yellow);
            Console.WriteLine();
            ChatTerminalRenderer.WriteColored($"  {question}", ConsoleColor.White);
            Console.WriteLine();
        }

        Console.WriteLine();
        ChatTerminalRenderer.WriteMuted("  Answer in the prompt below to continue...");
    }
}
