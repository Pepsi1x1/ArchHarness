namespace ArchHarness.App.Core;

/// <summary>
/// Well-known permission handler modes available to interactive setup and Copilot sessions.
/// </summary>
public static class PermissionHandlerModes
{
    public const string APPROVE_ALL = "approve-all";
    public const string PROMPT = "prompt";

    /// <summary>
    /// Normalizes a permission handler mode string to one of the well-known values.
    /// </summary>
    /// <param name="mode">The raw mode string to normalize.</param>
    /// <returns>The normalized permission handler mode.</returns>
    public static string Normalize(string? mode)
        => string.Equals(mode, PROMPT, StringComparison.OrdinalIgnoreCase)
            ? PROMPT
            : APPROVE_ALL;

    /// <summary>
    /// Cycles the permission handler mode by the given delta through the known modes.
    /// </summary>
    /// <param name="currentMode">The current mode string.</param>
    /// <param name="delta">The direction to cycle (positive = forward, negative = backward).</param>
    /// <returns>The next permission handler mode after cycling.</returns>
    public static string Next(string? currentMode, int delta)
    {
        string[] modes = new[] { APPROVE_ALL, PROMPT };
        string normalizedCurrentMode = Normalize(currentMode);
        int currentIndex = Array.FindIndex(modes, mode => string.Equals(mode, normalizedCurrentMode, StringComparison.OrdinalIgnoreCase));
        if (currentIndex < 0)
        {
            currentIndex = 0;
        }

        int nextIndex = (currentIndex + delta + modes.Length) % modes.Length;
        return modes[nextIndex];
    }
}
