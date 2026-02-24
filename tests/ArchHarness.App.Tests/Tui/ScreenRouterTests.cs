using ArchHarness.App.Tui;

namespace ArchHarness.App.Tests.Tui;

/// <summary>
/// Verifies that ScreenRouter correctly maps each ConsoleKey to the expected screen.
/// </summary>
public class ScreenRouterTests
{
    /// <summary>
    /// Each number key should navigate to its corresponding screen regardless of the current screen.
    /// </summary>
    [Theory]
    [InlineData(ConsoleKey.D1, TuiScreen.Artefacts, TuiScreen.ChatSetup)]
    [InlineData(ConsoleKey.D2, TuiScreen.ChatSetup, TuiScreen.RunMonitor)]
    [InlineData(ConsoleKey.D3, TuiScreen.RunMonitor, TuiScreen.Logs)]
    [InlineData(ConsoleKey.D4, TuiScreen.Logs, TuiScreen.Artefacts)]
    [InlineData(ConsoleKey.D5, TuiScreen.Artefacts, TuiScreen.Review)]
    [InlineData(ConsoleKey.D6, TuiScreen.Review, TuiScreen.Prompts)]
    public void Navigate_MapsKeyToExpectedScreen(ConsoleKey key, TuiScreen current, TuiScreen expected)
    {
        TuiScreen result = ScreenRouter.Navigate(key, current);

        Assert.Equal(expected, result);
    }

    /// <summary>
    /// An unmapped key should leave the current screen unchanged.
    /// </summary>
    [Fact]
    public void Navigate_UnmappedKey_ReturnsCurrentScreen()
    {
        TuiScreen result = ScreenRouter.Navigate(ConsoleKey.X, TuiScreen.Logs);

        Assert.Equal(TuiScreen.Logs, result);
    }

    /// <summary>
    /// Q and Escape are quit keys; all other keys are not.
    /// </summary>
    [Theory]
    [InlineData(ConsoleKey.Q, true)]
    [InlineData(ConsoleKey.Escape, true)]
    [InlineData(ConsoleKey.D1, false)]
    [InlineData(ConsoleKey.Enter, false)]
    public void IsQuitKey_ReturnsExpected(ConsoleKey key, bool expected)
    {
        bool result = ScreenRouter.IsQuitKey(key);

        Assert.Equal(expected, result);
    }
}
