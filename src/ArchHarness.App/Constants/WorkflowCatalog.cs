namespace ArchHarness.App.Constants;

/// <summary>
/// Describes a workflow that can be started from the console or web host.
/// </summary>
/// <param name="Id">Stable workflow identifier.</param>
/// <param name="Description">Short operator-facing description.</param>
/// <param name="DefaultTaskPrompt">Default task prompt used when the caller omits one.</param>
/// <param name="CliCommand">Suggested console command for invoking the workflow.</param>
public sealed record WorkflowDefinition(string Id, string Description, string DefaultTaskPrompt, string? CliCommand = null);

/// <summary>
/// Exposes the known workflow definitions.
/// </summary>
public static class WorkflowCatalog
{
    private static readonly WorkflowDefinition[] Definitions =
    {
        new(WorkflowNames.AUTO, "Default orchestrator-driven workflow.", DefaultPrompts.DEFAULT_TASK, "run <taskPrompt> <workspacePath> <workspaceMode> auto"),
        new(WorkflowNames.PLANNING, "Clarification and plan approval only.", DefaultPrompts.DEFAULT_TASK, "run <taskPrompt> <workspacePath> <workspaceMode> planning"),
        new(WorkflowNames.ARCHITECTURE_LOOP, "Architecture/security/style remediation loop.", DefaultPrompts.ARCHITECTURE_LOOP_TASK, "run <workspacePath> <workspaceMode> architecture-loop"),
        new(WorkflowNames.WIKIDOC, "Generate wiki documentation for discovered Git repositories.", DefaultPrompts.WIKIDOC_TASK, "wikidoc <scanRoot> [projectName] [modelOverrides]"),
        new(WorkflowNames.FRONTEND_FEATURE, "Legacy frontend-focused workflow.", DefaultPrompts.DEFAULT_TASK, "run <taskPrompt> <workspacePath> <workspaceMode> frontend_feature")
    };

    /// <summary>
    /// Returns the supported workflow definitions in presentation order.
    /// </summary>
    public static IReadOnlyList<WorkflowDefinition> GetAll()
        => Definitions;
}
