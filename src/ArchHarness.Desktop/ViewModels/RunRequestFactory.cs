using ArchHarness.App.Constants;
using ArchHarness.App.Core;

namespace ArchHarness.Desktop.ViewModels;

/// <summary>
/// Builds and validates <see cref="RunRequest"/> instances from desktop setup form state.
/// </summary>
public static class RunRequestFactory
{
    private const string DEFAULT_ARCH_LOOP_TASK_PROMPT = DefaultPrompts.ARCHITECTURE_LOOP_TASK;
    private const string APPROVE_ALL = PermissionHandlerModes.APPROVE_ALL;
    private const string PROMPT = PermissionHandlerModes.PROMPT;
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
            ModelOverrides: CliArgumentParser.ParseOverrides(modelOverridesText),
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
    /// Delegates to the shared implementation in <see cref="CliArgumentParser"/>.
    /// </summary>
    /// <param name="overrideText">The override text to parse.</param>
    /// <returns>A dictionary of overrides, or null if none were found.</returns>
    public static IDictionary<string, string>? ParseOverrides(string? overrideText)
        => CliArgumentParser.ParseOverrides(overrideText);
}
