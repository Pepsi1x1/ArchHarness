namespace ArchHarness.App.Tui;

/// <summary>
/// Decouples keyboard navigation logic from rendering, mapping console keys to screens.
/// </summary>
public static class ScreenRouter
{
    /// <summary>
    /// Returns the target screen for the given key press, or the current screen if the key is unmapped.
    /// </summary>
    /// <param name="key">The console key that was pressed.</param>
    /// <param name="currentScreen">The currently active screen.</param>
    /// <returns>The screen to navigate to.</returns>
    public static TuiScreen Navigate(ConsoleKey key, TuiScreen currentScreen)
    {
        return key switch
        {
            ConsoleKey.D1 => TuiScreen.ChatSetup,
            ConsoleKey.D2 => TuiScreen.RunMonitor,
            ConsoleKey.D3 => TuiScreen.Logs,
            ConsoleKey.D4 => TuiScreen.Artefacts,
            ConsoleKey.D5 => TuiScreen.Review,
            ConsoleKey.D6 => TuiScreen.Prompts,
            _ => currentScreen
        };
    }

    /// <summary>
    /// Returns true if the given key indicates a quit action.
    /// </summary>
    /// <param name="key">The console key that was pressed.</param>
    /// <returns>True when the key is Q or Escape.</returns>
    public static bool IsQuitKey(ConsoleKey key)
    {
        return key is ConsoleKey.Q or ConsoleKey.Escape;
    }
}
