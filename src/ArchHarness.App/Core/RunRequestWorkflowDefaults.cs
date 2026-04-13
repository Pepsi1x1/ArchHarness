using ArchHarness.App.Constants;

namespace ArchHarness.App.Core;

/// <summary>
/// Applies workflow-specific defaults to inbound run requests.
/// </summary>
public static class RunRequestWorkflowDefaults
{
    /// <summary>
    /// Returns a request with workflow-specific prompt and mode defaults populated.
    /// </summary>
    public static RunRequest Apply(RunRequest request)
    {
        string normalizedWorkflow = NormalizeWorkflow(request.Workflow);
        string workspaceMode = string.IsNullOrWhiteSpace(request.WorkspaceMode)
            ? WorkspaceModes.EXISTING_FOLDER
            : request.WorkspaceMode;

        return normalizedWorkflow switch
        {
            WorkflowNames.WIKIDOC => request with
            {
                Workflow = WorkflowNames.WIKIDOC,
                WorkspaceMode = workspaceMode,
                ReviewLoopAgents = new ReviewLoopAgentSelection(
                    CodingStyleEnabled: false,
                    SecurityEnabled: false,
                    ArchitectureEnabled: false),
                TaskPrompt = string.IsNullOrWhiteSpace(request.TaskPrompt)
                    ? DefaultPrompts.WIKIDOC_TASK
                    : request.TaskPrompt.Trim()
            },
            _ => request with
            {
                Workflow = normalizedWorkflow,
                WorkspaceMode = workspaceMode
            }
        };
    }

    private static string NormalizeWorkflow(string? workflow)
        => string.IsNullOrWhiteSpace(workflow)
            ? WorkflowNames.AUTO
            : workflow.Trim();
}
