namespace ArchHarness.App.Core;

/// <summary>
/// Defines terminal phase markers written to persisted run state.
/// </summary>
public static class RunTerminalPhases
{
    public const string COMPLETED = "completed";
    public const string INCOMPLETE = "incomplete";
    public const string FAILED = "failed";
    public const string PAUSED = "paused";
    public const string CANCELED = "canceled";
    public const string STOPPED = "stopped";
}
