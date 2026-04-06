namespace ArchHarness.App.Constants;

/// <summary>
/// Well-known workflow identifiers used when building and processing run requests.
/// </summary>
public static class WorkflowNames
{
    /// <summary>The default orchestrator-driven workflow.</summary>
    public const string AUTO = "auto";

    /// <summary>The workflow that performs clarification and plan approval without executing implementation steps.</summary>
    public const string PLANNING = "planning";

    /// <summary>The workflow that drives the architecture review remediation loop.</summary>
    public const string ARCHITECTURE_LOOP = "architecture-loop";

    /// <summary>The legacy workflow identifier used when no explicit workflow is provided by the CLI.</summary>
    public const string FRONTEND_FEATURE = "frontend_feature";
}