namespace ArchHarness.App.Core;

/// <summary>
/// Well-known permission handler modes available to interactive setup and Copilot sessions.
/// </summary>
internal static class PermissionHandlerModes
{
    public const string APPROVE_ALL = "approve-all";
    public const string PROMPT = "prompt";

    public static string Normalize(string? mode)
        => string.Equals(mode, PROMPT, StringComparison.OrdinalIgnoreCase)
            ? PROMPT
            : APPROVE_ALL;

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