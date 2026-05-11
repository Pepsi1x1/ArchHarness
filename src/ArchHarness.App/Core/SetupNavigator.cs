using ArchHarness.App.Constants;

namespace ArchHarness.App.Core;

/// <summary>
/// Handles keyboard navigation and mode toggling for the interactive setup form.
/// </summary>
internal static class SetupNavigator
{
    private const string EXISTING_FOLDER_MODE = WorkspaceModes.EXISTING_FOLDER;
    private const string NEW_PROJECT_MODE = WorkspaceModes.NEW_PROJECT;
    private const string EXISTING_GIT_MODE = WorkspaceModes.EXISTING_GIT;

    /// <summary>
    /// Attempts to handle an up/down arrow navigation key, skipping non-interactive fields.
    /// </summary>
    /// <param name="key">The console key pressed.</param>
    /// <param name="fields">The current setup fields.</param>
    /// <param name="selectedIndex">The current selection index (updated in-place).</param>
    /// <returns>True if the key was handled as navigation.</returns>
    public static bool TryHandleNavigation(ConsoleKey key, IReadOnlyList<SetupField> fields, ref int selectedIndex)
    {
        if (key == ConsoleKey.UpArrow)
        {
            selectedIndex = MoveSelection(fields, selectedIndex, -1);
            return true;
        }

        if (key == ConsoleKey.DownArrow)
        {
            selectedIndex = MoveSelection(fields, selectedIndex, 1);
            return true;
        }

        return false;
    }

    private static int MoveSelection(IReadOnlyList<SetupField> fields, int selectedIndex, int direction)
    {
        int newIndex = WrapIndex(selectedIndex + direction, fields.Count);
        while (newIndex != selectedIndex && IsNonInteractiveField(fields[newIndex].Id))
        {
            newIndex = WrapIndex(newIndex + direction, fields.Count);
        }

        return newIndex;
    }

    private static int WrapIndex(int index, int count)
        => (index + count) % count;

    /// <summary>
    /// Returns true when the field ID identifies a non-interactive section header.
    /// </summary>
    /// <param name="fieldId">The field identifier to check.</param>
    /// <returns>True if the field is non-interactive.</returns>
    public static bool IsNonInteractiveField(string fieldId)
        => fieldId.StartsWith("__section__", StringComparison.Ordinal);

    /// <summary>
    /// Attempts to toggle a mode field using left/right arrow keys.
    /// </summary>
    /// <param name="key">The console key pressed.</param>
    /// <param name="field">The currently selected field.</param>
    /// <param name="draft">The draft to update.</param>
    /// <returns>True if the key was handled as a toggle.</returns>
    public static bool TryHandleModeToggle(ConsoleKey key, SetupField field, SetupDraft draft)
    {
        if (key is not (ConsoleKey.LeftArrow or ConsoleKey.RightArrow))
        {
            return false;
        }

        if (field.Id == "WorkspaceMode")
        {
            draft.WorkspaceMode = NextMode(draft.WorkspaceMode, key == ConsoleKey.RightArrow ? 1 : -1);
            return true;
        }

        if (field.Id == "PermissionHandlerMode")
        {
            draft.PermissionHandlerMode = PermissionHandlerModes.Next(draft.PermissionHandlerMode, key == ConsoleKey.RightArrow ? 1 : -1);
            return true;
        }

        if (field.Id == "ReviewLoopCodingStyleEnabled")
        {
            draft.ReviewLoopCodingStyleEnabled = !draft.ReviewLoopCodingStyleEnabled;
            return true;
        }

        if (field.Id == "ReviewLoopSecurityEnabled")
        {
            draft.ReviewLoopSecurityEnabled = !draft.ReviewLoopSecurityEnabled;
            return true;
        }

        if (field.Id == "ReviewLoopArchitectureEnabled")
        {
            draft.ReviewLoopArchitectureEnabled = !draft.ReviewLoopArchitectureEnabled;
            return true;
        }

        if (field.Id == "ArchitectureLoopMode")
        {
            draft.ArchitectureLoopMode = !draft.ArchitectureLoopMode;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Cycles through workspace modes by the given delta.
    /// </summary>
    /// <param name="currentMode">The current mode string.</param>
    /// <param name="delta">Direction to cycle (+1 or -1).</param>
    /// <returns>The next mode string.</returns>
    public static string NextMode(string currentMode, int delta)
    {
        string[] modes = new[] { NEW_PROJECT_MODE, EXISTING_FOLDER_MODE, EXISTING_GIT_MODE };
        int currentIndex = Array.FindIndex(modes, m => string.Equals(m, currentMode, StringComparison.OrdinalIgnoreCase));
        if (currentIndex < 0)
        {
            currentIndex = 1;
        }

        int next = (currentIndex + delta + modes.Length) % modes.Length;
        return modes[next];
    }
}
