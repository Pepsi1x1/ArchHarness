namespace ArchHarness.App.Core;

/// <summary>
/// Defines persisted phase names for resumable run execution.
/// </summary>
public static class RunPhases
{
    public const string PLANNING = "planning";
    public const string EXECUTING_PLAN = "executing-plan";
    public const string ARCHITECTURE_LOOP = "architecture-loop";
    public const string FINALIZING = "finalizing";
}