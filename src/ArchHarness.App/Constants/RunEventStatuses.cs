namespace ArchHarness.App.Constants;

/// <summary>
/// Well-known status values used in step and orchestration event payloads.
/// </summary>
public static class RunEventStatuses
{
    /// <summary>Status emitted when a step or event completes successfully.</summary>
    public const string COMPLETED = "completed";

    /// <summary>Status emitted when a step or event fails.</summary>
    public const string FAILED = "failed";

    /// <summary>Status emitted when an architecture loop stops early without progress.</summary>
    public const string BLOCKED = "blocked";
}