namespace ArchHarness.App.Core;

/// <summary>
/// Handles keyboard navigation and mode toggling for the interactive setup form.
/// </summary>
internal static class SetupNavigator
{
    private const string EXISTING_FOLDER_MODE = "existing-folder";
    private const string NEW_PROJECT_MODE = "new-project";
    private const string EXISTING_GIT_MODE = "existing-git";

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
            int newIdx = selectedIndex == 0 ? fields.Count - 1 : selectedIndex - 1;
            while (newIdx != selectedIndex && IsNonInteractiveField(fields[newIdx].Id))
            {
                newIdx = newIdx == 0 ? fields.Count - 1 : newIdx - 1;
            }

            selectedIndex = newIdx;
            return true;
        }

        if (key == ConsoleKey.DownArrow)
        {
            int newIdx = selectedIndex == fields.Count - 1 ? 0 : selectedIndex + 1;
            while (newIdx != selectedIndex && IsNonInteractiveField(fields[newIdx].Id))
            {
                newIdx = newIdx == fields.Count - 1 ? 0 : newIdx + 1;
            }

            selectedIndex = newIdx;
            return true;
        }

        return false;
    }

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
