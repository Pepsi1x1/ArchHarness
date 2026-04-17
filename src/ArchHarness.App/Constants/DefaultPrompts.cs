namespace ArchHarness.App.Constants;

/// <summary>
/// Default prompt text constants used by the orchestrator, CLI, setup editor, and desktop hosts.
/// </summary>
public static class DefaultPrompts
{
    /// <summary>Default task prompt for standard (non-architecture-loop) runs.</summary>
    public const string DEFAULT_TASK = "Implement requested change";

    /// <summary>Default task prompt used when architecture loop mode is active.</summary>
    public const string ARCHITECTURE_LOOP_TASK = "Run coding style, security, and architecture review loop for the existing workspace and apply required remediation.";

    /// <summary>Default task prompt used by the wikidoc workflow.</summary>
    public const string WIKIDOC_TASK = "Generate repository wiki documentation for all discovered Git repositories under the scan root and synthesize a megawiki with shared concept pages.";
}
