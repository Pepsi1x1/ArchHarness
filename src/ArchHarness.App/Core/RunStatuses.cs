namespace ArchHarness.App.Core;

/// <summary>
/// Defines status values for persisted and live run state contracts.
/// </summary>
public static class RunStatuses
{
    public const string IDLE = "idle";
    public const string STARTING = "starting";
    public const string RESUMING = "resuming";
    public const string RUNNING = "running";
    public const string CANCELING = "canceling";
    public const string COMPLETED = "completed";
    public const string CANCELED = "canceled";
    public const string STOPPED = "stopped";
    public const string FAILED = "failed";
}