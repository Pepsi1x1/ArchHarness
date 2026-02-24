using ArchHarness.App.Core;

namespace ArchHarness.App.Tui;

/// <summary>
/// Renders content screens: splash, prompts, artefacts, and file viewer.
/// </summary>
public static class ContentScreenRenderer
{
    /// <summary>
    /// Renders the startup splash screen with the ASCII art banner.
    /// </summary>
    public static void RenderSplash()
    {
        Console.Clear();
        Console.CursorVisible = false;
        int width = Math.Max(60, Console.WindowWidth - 1);
        string banner = @"    _    ____   ____ _   _
   / \  |  _ \ / ___| | | |
  / _ \ | |_) | |   | |_| |
 / ___ \|  _ <| |___|  _  |
/_/   \_\_| \_\\____|_| |_|
 _   _    _    ____  _   _ _____ ____ ____ 
| | | |  / \  |  _ \| \ | | ____/ ___/ ___|
| |_| | / _ \ | |_) |  \| |  _| \___ \___ \
|  _  |/ ___ \|  _ <| |\  | |___ ___) |__) |
|_| |_/_/   \_\_| \_\_| \_|_____|____/____/";

        Console.WriteLine();
        ChatTerminalRenderer.WriteHRule(width);
        foreach (string line in banner.Split(Environment.NewLine))
        {
            ChatTerminalRenderer.WriteCenteredColored(line, width, ConsoleColor.Cyan);
        }

        ChatTerminalRenderer.WriteCenteredColored("AI-Orchestrated Developer Harness", width, ConsoleColor.Cyan);
        ChatTerminalRenderer.WriteHRule(width);
        Console.WriteLine();
        ChatTerminalRenderer.WriteColored("  Checking prerequisites", ConsoleColor.DarkGray);
        ChatTerminalRenderer.WriteColored("...", ConsoleColor.DarkGray);
        Console.WriteLine();
        Console.WriteLine();
    }

    /// <summary>
    /// Renders the delegated prompts screen showing captured agent prompts.
    /// </summary>
    /// <param name="events">The thread-safe list of progress events.</param>
    public static void RenderPromptsScreen(List<RuntimeProgressEvent> events)
    {
        Console.Clear();
        int width = Math.Max(60, Console.WindowWidth - 1);

        ChatTerminalRenderer.WriteScreenTitle("Delegated Prompts", width);

        List<RuntimeProgressEvent> prompts;
        lock (events)
        {
            prompts = events.Where(e => !string.IsNullOrWhiteSpace(e.Prompt)).ToList();
        }

        if (prompts.Count == 0)
        {
            ChatTerminalRenderer.WriteMuted("  No delegated prompts captured yet.");
            return;
        }

        foreach (RuntimeProgressEvent evt in prompts.TakeLast(20))
        {
            ChatTerminalRenderer.WriteColored($"  [{evt.TimestampUtc:HH:mm:ss}] ", ConsoleColor.Cyan);
            ChatTerminalRenderer.WriteColored(evt.Source, ConsoleColor.White);
            Console.WriteLine();
            ChatTerminalRenderer.WriteMuted($"  {evt.Prompt}");
            Console.WriteLine();
        }
    }

    /// <summary>
    /// Renders the artefacts listing screen for the given run directory.
    /// </summary>
    /// <param name="runDirectory">The path to the run output directory.</param>
    public static void RenderArtefactsScreen(string runDirectory)
    {
        Console.Clear();
        int width = Math.Max(60, Console.WindowWidth - 1);

        ChatTerminalRenderer.WriteScreenTitle("Artefacts", width);
        ChatTerminalRenderer.WriteMuted($"  {runDirectory}");
        Console.WriteLine();
        foreach (string file in Directory.GetFiles(runDirectory).OrderBy(Path.GetFileName))
        {
            ChatTerminalRenderer.WriteColored("  > ", ConsoleColor.Cyan);
            Console.WriteLine(Path.GetFileName(file));
        }
    }

    /// <summary>
    /// Renders a file viewer screen displaying the tail of the specified file.
    /// </summary>
    /// <param name="title">The screen title to display.</param>
    /// <param name="path">The path to the file to display.</param>
    /// <param name="maxLines">The maximum number of lines to show from the end of the file.</param>
    public static void RenderFileScreen(string title, string path, int maxLines)
    {
        Console.Clear();
        int width = Math.Max(60, Console.WindowWidth - 1);

        ChatTerminalRenderer.WriteScreenTitle(title, width);
        ChatTerminalRenderer.WriteMuted($"  {path}");
        Console.WriteLine();

        if (!File.Exists(path))
        {
            ChatTerminalRenderer.WriteColored("  File not found.", ConsoleColor.Yellow);
            Console.WriteLine();
            return;
        }

        foreach (string line in File.ReadLines(path).TakeLast(maxLines))
        {
            Console.WriteLine(line);
        }
    }
}
