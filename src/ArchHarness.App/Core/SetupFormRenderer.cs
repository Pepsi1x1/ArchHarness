using ArchHarness.App.Tui;

namespace ArchHarness.App.Core;

/// <summary>
/// Renders the interactive setup form in the terminal, including field rows and validation indicators.
/// </summary>
internal static class SetupFormRenderer
{
    private const string NoneText = "(none)";
    private static int _spinnerFrame;
    private static readonly char[] SpinnerFrames = ['|', '/', '-', '\\'];

    /// <summary>
    /// Renders the full interactive setup form panel with field rows and keyboard hints.
    /// </summary>
    /// <param name="fields">The list of setup fields to render.</param>
    /// <param name="selectedIndex">The index of the currently selected field.</param>
    /// <param name="validationError">The field ID of a validation error to highlight, or null.</param>
    public static void RenderSetupForm(IReadOnlyList<SetupField> fields, int selectedIndex, string? validationError)
    {
        Console.Clear();
        char spinnerChar = SpinnerFrames[_spinnerFrame % SpinnerFrames.Length];
        _spinnerFrame++;

        int width = Math.Max(60, Console.WindowWidth - 1);
        int panelWidth = Math.Max(56, Math.Min(width - 4, 110));
        int contentWidth = Math.Max(24, panelWidth - 2);

        // +---+ top border
        ChatTerminalRenderer.WriteColored("  +", ConsoleColor.Cyan);
        ChatTerminalRenderer.WriteColored(new string('-', contentWidth), ConsoleColor.Cyan);
        ChatTerminalRenderer.WriteColored("+", ConsoleColor.Cyan);
        Console.WriteLine();

        // Title row with spinner
        string titleLine = $"  ARCH HARNESS  {spinnerChar}";
        ChatTerminalRenderer.WriteColored("  |", ConsoleColor.Cyan);
        ChatTerminalRenderer.WriteColored(titleLine.PadRight(contentWidth), ConsoleColor.Cyan);
        ChatTerminalRenderer.WriteColored("|", ConsoleColor.Cyan);
        Console.WriteLine();

        // Subtitle row
        ChatTerminalRenderer.WriteColored("  |", ConsoleColor.Cyan);
        ChatTerminalRenderer.WriteColored("  * CHAT SETUP *".PadRight(contentWidth), ConsoleColor.DarkCyan);
        ChatTerminalRenderer.WriteColored("|", ConsoleColor.Cyan);
        Console.WriteLine();

        // +---+ separator
        ChatTerminalRenderer.WriteColored("  +", ConsoleColor.Cyan);
        ChatTerminalRenderer.WriteColored(new string('-', contentWidth), ConsoleColor.Cyan);
        ChatTerminalRenderer.WriteColored("+", ConsoleColor.Cyan);
        Console.WriteLine();

        // Field rows
        for (var i = 0; i < fields.Count; i++)
        {
            WriteInteractiveSetupRow(contentWidth, fields[i], isSelected: i == selectedIndex, isError: fields[i].Id == validationError);
        }

        // +---+ separator before footer
        ChatTerminalRenderer.WriteColored("  +", ConsoleColor.Cyan);
        ChatTerminalRenderer.WriteColored(new string('-', contentWidth), ConsoleColor.Cyan);
        ChatTerminalRenderer.WriteColored("+", ConsoleColor.Cyan);
        Console.WriteLine();

        // Keyboard hint footer
        var hint = " [Up/Down] Navigate  [Enter] Edit  [Left/Right] Cycle  [F5] Run  [Esc] Cancel";
        var paddedHint = hint.Length <= contentWidth ? hint.PadRight(contentWidth) : hint[..contentWidth];
        ChatTerminalRenderer.WriteColored("  |", ConsoleColor.Cyan);
        ChatTerminalRenderer.WriteColored(paddedHint, ConsoleColor.DarkCyan);
        ChatTerminalRenderer.WriteColored("|", ConsoleColor.Cyan);
        Console.WriteLine();

        // +---+ bottom border
        ChatTerminalRenderer.WriteColored("  +", ConsoleColor.Cyan);
        ChatTerminalRenderer.WriteColored(new string('-', contentWidth), ConsoleColor.Cyan);
        ChatTerminalRenderer.WriteColored("+", ConsoleColor.Cyan);
        Console.WriteLine();

        // Validation error status line
        if (validationError != null)
        {
            var errorField = fields.FirstOrDefault(f => f.Id == validationError);
            var errorLabel = errorField?.Label ?? validationError;
            Console.WriteLine();
            ChatTerminalRenderer.WriteColored($"  ! Field required: {errorLabel}", ConsoleColor.Yellow);
            Console.WriteLine();
        }
    }

    /// <summary>
    /// Displays a brief "saved" indicator on the console.
    /// </summary>
    public static void FlashSaved()
    {
        Console.ForegroundColor = ConsoleColor.DarkGreen;
        Console.Write("  [OK] Saved");
        Console.ResetColor();
        Thread.Sleep(350);
    }

    private static void WriteInteractiveSetupRow(int contentWidth, SetupField field, bool isSelected, bool isError)
    {
        if (field.Id.StartsWith("__section__", StringComparison.Ordinal))
        {
            var sectionLabel = $" {field.Label} ";
            var padding = Math.Max(0, contentWidth - sectionLabel.Length);
            var leftPad = padding / 2;
            var paddedLabel = new string('─', leftPad) + sectionLabel + new string('─', padding - leftPad);
            ChatTerminalRenderer.WriteColored("  |", ConsoleColor.Cyan);
            ChatTerminalRenderer.WriteColored(paddedLabel.PadRight(contentWidth), ConsoleColor.DarkCyan);
            ChatTerminalRenderer.WriteColored("|", ConsoleColor.Cyan);
            Console.WriteLine();
            return;
        }

        var isPlaceholder = string.IsNullOrWhiteSpace(field.Value) || field.Value == NoneText || field.IsPlaceholderValue;
        var displayValue = isPlaceholder && !field.IsPlaceholderValue ? NoneText : field.Value;
        var icon = GetFieldIconForId(field.Id);
        string selectionMarker;
        if (isError)
        {
            selectionMarker = "x";
        }
        else if (isSelected)
        {
            selectionMarker = ">";
        }
        else
        {
            selectionMarker = " ";
        }
        var labelText = $" {selectionMarker} {icon} {field.Label.PadRight(16)} ";
        var availableValueWidth = Math.Max(8, contentWidth - labelText.Length);

        if (displayValue.Length > availableValueWidth)
        {
            displayValue = displayValue[..Math.Max(0, availableValueWidth - 3)] + "...";
        }

        ChatTerminalRenderer.WriteColored("  |", ConsoleColor.Cyan);

        if (isError)
        {
            ChatTerminalRenderer.WriteColored(labelText, ConsoleColor.Red);
            ChatTerminalRenderer.WriteColored(displayValue.PadRight(availableValueWidth), ConsoleColor.Red);
        }
        else if (isSelected)
        {
            ChatTerminalRenderer.WriteColored(labelText, ConsoleColor.Yellow);
            ChatTerminalRenderer.WriteColored(displayValue.PadRight(availableValueWidth), isPlaceholder ? ConsoleColor.DarkGray : ConsoleColor.Yellow);
        }
        else
        {
            ChatTerminalRenderer.WriteColored(labelText, ConsoleColor.DarkGray);
            ChatTerminalRenderer.WriteColored(displayValue.PadRight(availableValueWidth), isPlaceholder ? ConsoleColor.DarkGray : ConsoleColor.DarkMagenta);
        }

        ChatTerminalRenderer.WriteColored("|", ConsoleColor.Cyan);
        Console.WriteLine();
    }

    private static string GetFieldIconForId(string fieldId)
    {
        return fieldId switch
        {
            "WorkspacePath"        => "W",
            "WorkspaceMode"        => "M",
            "BuildCommand"         => "B",
            "ArchitectureLoopMode" => "A",
            "ArchitectureLoopPrompt" => "P",
            _                      => ">",
        };
    }
}
