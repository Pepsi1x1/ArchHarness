namespace ArchHarness.App.Tui;

/// <summary>
/// Shared terminal rendering primitives used across multiple screen renderers.
/// </summary>
public static class ChatTerminalRenderer
{
    // ── Drawing Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Writes a screen title with horizontal rules above and below.
    /// </summary>
    /// <param name="title">The title text to display.</param>
    /// <param name="width">The available console width.</param>
    public static void WriteScreenTitle(string title, int width)
    {
        WriteHRule(width);
        WriteColored($"  {title.ToUpperInvariant()}", ConsoleColor.Cyan);
        Console.WriteLine();
        WriteHRule(width);
        Console.WriteLine();
    }

    /// <summary>
    /// Writes a horizontal rule spanning the given width.
    /// </summary>
    /// <param name="width">The width of the rule.</param>
    /// <param name="color">The foreground color for the rule.</param>
    public static void WriteHRule(int width, ConsoleColor color = ConsoleColor.Cyan)
    {
        WriteColored(new string('-', Math.Max(1, width)), color);
        Console.WriteLine();
    }

    /// <summary>
    /// Writes text in the specified foreground color and resets afterwards.
    /// </summary>
    /// <param name="text">The text to write.</param>
    /// <param name="color">The foreground color.</param>
    public static void WriteColored(string text, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ResetColor();
    }

    /// <summary>
    /// Writes a line of muted (dark gray) text.
    /// </summary>
    /// <param name="text">The text to write.</param>
    public static void WriteMuted(string text)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(text);
        Console.ResetColor();
    }

    /// <summary>
    /// Writes centered text in the specified color, padded to the given width.
    /// </summary>
    /// <param name="text">The text to center.</param>
    /// <param name="width">The total width to pad to.</param>
    /// <param name="color">The foreground color.</param>
    public static void WriteCenteredColored(string text, int width, ConsoleColor color)
    {
        string padded = text.PadLeft((width + text.Length) / 2).PadRight(width);
        Console.ForegroundColor = color;
        Console.WriteLine(padded);
        Console.ResetColor();
    }

    /// <summary>
    /// Writes a label-value pair with the label in dark gray and the value in a specified color.
    /// </summary>
    /// <param name="label">The label text.</param>
    /// <param name="value">The value text.</param>
    /// <param name="valueColor">The foreground color for the value.</param>
    public static void WriteLabel(string label, string value, ConsoleColor valueColor = ConsoleColor.White)
    {
        WriteColored($"  {label}  ", ConsoleColor.DarkGray);
        WriteColored(value, valueColor);
        Console.WriteLine();
    }

    /// <summary>
    /// Writes a navigation key indicator in the footer.
    /// </summary>
    /// <param name="key">The key label (e.g. "1").</param>
    /// <param name="label">The description of the key action.</param>
    public static void WriteNavKey(string key, string label)
    {
        WriteColored("  [", ConsoleColor.DarkGray);
        WriteColored(key, ConsoleColor.Cyan);
        WriteColored($"] {label}", ConsoleColor.DarkGray);
    }


    /// <summary>
    /// Returns a color based on the message content for event highlighting.
    /// </summary>
    /// <param name="message">The event message to evaluate.</param>
    /// <returns>A console color appropriate for the message severity.</returns>
    public static ConsoleColor GetEventColor(string message)
    {
        if (message.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("fail", StringComparison.OrdinalIgnoreCase))
        {
            return ConsoleColor.Yellow;
        }

        if (message.Contains("complet", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("success", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("pass", StringComparison.OrdinalIgnoreCase))
        {
            return ConsoleColor.Cyan;
        }

        return ConsoleColor.White;
    }

    /// <summary>
    /// Summarizes a prompt string to a maximum display length for compact rendering.
    /// </summary>
    /// <param name="prompt">The full prompt text.</param>
    /// <returns>The prompt truncated to 120 characters with an ellipsis if needed.</returns>
    public static string SummarizePrompt(string prompt)
    {
        string trimmed = prompt.Replace(Environment.NewLine, " ").Trim();
        return trimmed.Length <= 120 ? trimmed : trimmed[..117] + "...";
    }
}
