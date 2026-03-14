using ArchHarness.App.Core;

namespace ArchHarness.Desktop.ViewModels;

/// <summary>
/// Builds and validates <see cref="RunRequest"/> instances from desktop setup form state.
/// </summary>
public static class RunRequestFactory
{
    private const string DEFAULT_ARCH_LOOP_TASK_PROMPT = "Run coding style, security, and architecture review loop for the existing workspace and apply required remediation.";
    private const string APPROVE_ALL = "approve-all";
    private const string PROMPT = "prompt";

    /// <summary>
    /// Attempts to build a <see cref="RunRequest"/> from the provided setup values,
    /// returning null with a validation message when required fields are missing.
    /// </summary>
    /// <param name="taskPrompt">The task prompt text.</param>
    /// <param name="workspacePath">The workspace path.</param>
    /// <param name="workspaceMode">The workspace initialization mode.</param>
    /// <param name="workflow">The workflow identifier.</param>
    /// <param name="projectName">Optional project name.</param>
    /// <param name="modelOverridesText">Comma-separated model override text.</param>
    /// <param name="buildCommand">Optional build command.</param>
    /// <param name="permissionHandlerMode">The permission approval mode.</param>
    /// <param name="reviewLoopCodingStyleEnabled">Whether coding style review is enabled.</param>
    /// <param name="reviewLoopSecurityEnabled">Whether security review is enabled.</param>
    /// <param name="reviewLoopArchitectureEnabled">Whether architecture review is enabled.</param>
    /// <param name="architectureLoopMode">Whether iterative architecture loop mode is active.</param>
    /// <param name="architectureLoopPrompt">Optional supplementary prompt for architecture loop iterations.</param>
    /// <param name="validationMessage">When the return value is null, contains the validation error.</param>
    /// <returns>A valid <see cref="RunRequest"/>, or null if validation fails.</returns>
    public static RunRequest? TryBuild(
        string taskPrompt,
        string workspacePath,
        string workspaceMode,
        string workflow,
        string projectName,
        string modelOverridesText,
        string buildCommand,
        string permissionHandlerMode,
        bool reviewLoopCodingStyleEnabled,
        bool reviewLoopSecurityEnabled,
        bool reviewLoopArchitectureEnabled,
        bool architectureLoopMode,
        string architectureLoopPrompt,
        out string? validationMessage)
    {
        validationMessage = null;
        string resolvedTaskPrompt;
        if (architectureLoopMode)
        {
            resolvedTaskPrompt = string.IsNullOrWhiteSpace(taskPrompt) ? DEFAULT_ARCH_LOOP_TASK_PROMPT : taskPrompt.Trim();
        }
        else
        {
            resolvedTaskPrompt = string.IsNullOrWhiteSpace(taskPrompt) ? string.Empty : taskPrompt.Trim();
        }

        if (string.IsNullOrWhiteSpace(resolvedTaskPrompt))
        {
            validationMessage = "Task prompt is required unless architecture loop mode is using its default task.";
            return null;
        }

        string resolvedWorkspacePath = string.IsNullOrWhiteSpace(workspacePath) ? Environment.CurrentDirectory : workspacePath.Trim();
        string resolvedWorkspaceMode = string.IsNullOrWhiteSpace(workspaceMode) ? "existing-folder" : workspaceMode;
        string resolvedWorkflow;
        if (architectureLoopMode)
        {
            resolvedWorkflow = "architecture-loop";
        }
        else
        {
            resolvedWorkflow = string.IsNullOrWhiteSpace(workflow) ? "auto" : workflow.Trim();
        }

        return new RunRequest(
            TaskPrompt: resolvedTaskPrompt,
            WorkspacePath: resolvedWorkspacePath,
            WorkspaceMode: resolvedWorkspaceMode,
            Workflow: resolvedWorkflow,
            ProjectName: string.IsNullOrWhiteSpace(projectName) ? null : projectName.Trim(),
            ModelOverrides: ParseOverrides(modelOverridesText),
            BuildCommand: string.IsNullOrWhiteSpace(buildCommand) ? null : buildCommand.Trim(),
            PermissionHandlerMode: NormalizePermissionMode(permissionHandlerMode),
            ReviewLoopAgents: new ReviewLoopAgentSelection(
                reviewLoopCodingStyleEnabled,
                reviewLoopSecurityEnabled,
                reviewLoopArchitectureEnabled),
            ArchitectureLoopMode: architectureLoopMode,
            ArchitectureLoopPrompt: string.IsNullOrWhiteSpace(architectureLoopPrompt) ? null : architectureLoopPrompt.Trim());
    }

    /// <summary>
    /// Normalizes a permission mode string to one of the known values.
    /// </summary>
    /// <param name="mode">The raw permission mode string.</param>
    /// <returns>The normalized permission mode.</returns>
    public static string NormalizePermissionMode(string? mode)
        => string.Equals(mode, PROMPT, StringComparison.OrdinalIgnoreCase) ? PROMPT : APPROVE_ALL;

    /// <summary>
    /// Parses a comma-separated "role=model" override string into a dictionary.
    /// </summary>
    /// <param name="overrideText">The override text to parse.</param>
    /// <returns>A dictionary of overrides, or null if none were found.</returns>
    public static IDictionary<string, string>? ParseOverrides(string? overrideText)
    {
        if (string.IsNullOrWhiteSpace(overrideText))
        {
            return null;
        }

        Dictionary<string, string> output = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string[] segments = overrideText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string segment in segments)
        {
            int idx = segment.IndexOf('=');
            if (idx <= 0 || idx == segment.Length - 1)
            {
                continue;
            }

            string role = segment[..idx].Trim();
            string model = segment[(idx + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(role) && !string.IsNullOrWhiteSpace(model))
            {
                output[role] = model;
            }
        }

        return output.Count == 0 ? null : output;
    }
}
