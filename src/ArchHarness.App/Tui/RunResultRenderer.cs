using ArchHarness.App.Copilot;

namespace ArchHarness.App.Tui;

/// <summary>
/// Renders terminal screens for run outcomes: preflight failure, run failure, and exit.
/// </summary>
public static class RunResultRenderer
{
    /// <summary>
    /// Renders the preflight failure screen with actionable fix steps.
    /// </summary>
    /// <param name="result">The preflight validation result containing the summary and fix steps.</param>
    public static void RenderPreflightFailure(PreflightValidationResult result)
    {
        Console.Clear();
        int width = Math.Max(60, Console.WindowWidth - 1);

        Console.BackgroundColor = ConsoleColor.DarkYellow;
        Console.ForegroundColor = ConsoleColor.Black;
        string banner = "  x Startup Preflight Failed";
        Console.WriteLine(banner + new string(' ', Math.Max(0, width - banner.Length)));
        Console.ResetColor();
        Console.WriteLine();
        ChatTerminalRenderer.WriteColored($"  {result.Summary}", ConsoleColor.Yellow);
        Console.WriteLine();
        Console.WriteLine();
        ChatTerminalRenderer.WriteColored("  Actionable fixes:", ConsoleColor.Yellow);
        Console.WriteLine();
        foreach (string step in result.FixSteps)
        {
            ChatTerminalRenderer.WriteColored("  > ", ConsoleColor.Yellow);
            Console.WriteLine(step);
        }

        Console.WriteLine();
        ChatTerminalRenderer.WriteMuted("  Press any key to exit.");
        Console.CursorVisible = true;
        WaitForExitInputIfInteractive();
    }

    /// <summary>
    /// Renders the run failure screen with hints for common issues.
    /// </summary>
    /// <param name="ex">The exception that caused the run to fail.</param>
    public static void RenderRunFailure(Exception ex)
    {
        Console.Clear();
        int width = Math.Max(60, Console.WindowWidth - 1);

        Console.BackgroundColor = ConsoleColor.DarkYellow;
        Console.ForegroundColor = ConsoleColor.Black;
        string banner = "  x Run Failed";
        Console.WriteLine(banner + new string(' ', Math.Max(0, width - banner.Length)));
        Console.ResetColor();
        Console.WriteLine();
        ChatTerminalRenderer.WriteColored($"  {ex.Message}", ConsoleColor.Yellow);
        Console.WriteLine();
        Console.WriteLine();
        ChatTerminalRenderer.WriteColored("  Hints:", ConsoleColor.Yellow);
        Console.WriteLine();
        ChatTerminalRenderer.WriteColored("  > ", ConsoleColor.Yellow);
        Console.WriteLine("If timeout mentions AwaitingUserInput=True, answer the active question prompt quickly.");
        ChatTerminalRenderer.WriteColored("  > ", ConsoleColor.Yellow);
        Console.WriteLine("If timeout repeats, increase copilot.sessionResponseTimeoutSeconds in appsettings.json.");
        ChatTerminalRenderer.WriteColored("  > ", ConsoleColor.Yellow);
        Console.WriteLine("If auth errors persist, run `copilot` then `/login`, then rerun ArchHarness.");
        Console.WriteLine();
        ChatTerminalRenderer.WriteMuted("  Press any key to exit.");
        Console.CursorVisible = true;
        WaitForExitInputIfInteractive();
    }

    /// <summary>
    /// Renders the goodbye/exit message.
    /// </summary>
    public static void RenderExitMessage()
    {
        Console.Clear();
        Console.CursorVisible = true;
        int width = Math.Max(60, Console.WindowWidth - 1);
        Console.WriteLine();
        ChatTerminalRenderer.WriteHRule(width);
        ChatTerminalRenderer.WriteCenteredColored("Thanks for using ArchHarness", width, ConsoleColor.Cyan);
        ChatTerminalRenderer.WriteHRule(width);
        Console.WriteLine();
    }

    private static void WaitForExitInputIfInteractive()
    {
        try
        {
            if (!Console.IsInputRedirected)
            {
                Console.ReadKey(intercept: true);
            }
        }
        catch (IOException)
        {
            // Ignore input errors in non-interactive environments.
        }
        catch (InvalidOperationException)
        {
            // Ignore when no interactive console is available.
        }
    }
}
