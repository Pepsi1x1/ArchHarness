namespace ArchHarness.App.Core;

/// <summary>
/// Parses CLI arguments and override strings into RunRequest components.
/// </summary>
internal static class CliArgumentParser
{
    private const string DEFAULT_ARCH_LOOP_TASK_PROMPT = "Run architecture and style review loop for the existing workspace and apply required remediation.";

    /// <summary>
    /// Attempts to parse CLI arguments into a RunRequest. Returns null when arguments
    /// do not match the expected 'run' command format.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <param name="agentsOptions">Agent configuration used to resolve architecture loop settings.</param>
    /// <returns>A RunRequest if parsing succeeds, or null.</returns>
    public static RunRequest? TryParseCliArgs(string[] args, AgentsOptions agentsOptions)
    {
        if (args.Length < 3 || !args[0].Equals("run", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        bool architectureLoopMode = agentsOptions.Architecture.ArchitectureLoopMode;
        string? architectureLoopPrompt = NormalizeArchitectureLoopPrompt(agentsOptions.Architecture.ArchitectureLoopPrompt);

        if (architectureLoopMode)
        {
            // In architecture-loop mode, support both:
            // 1) run <workspacePath> <workspaceMode> [workflow] [projectName] [overrides] [buildCommand]
            // 2) run <taskPrompt> <workspacePath> <workspaceMode> [workflow] [projectName] [overrides] [buildCommand]
            if (TryParseLoopModePromptless(args, architectureLoopPrompt, out RunRequest? promptlessRequest))
            {
                return promptlessRequest;
            }

            if (TryParseStandardShape(args, architectureLoopPrompt, forceLoopWorkflow: true, out RunRequest? standardLoopRequest))
            {
                return standardLoopRequest;
            }

            return null;
        }

        if (args.Length < 4)
        {
            return null;
        }

        if (!TryParseStandardShape(args, architectureLoopPrompt, forceLoopWorkflow: false, out RunRequest? standardRequest))
        {
            return null;
        }

        return standardRequest;
    }

    private static bool TryParseLoopModePromptless(
        string[] args,
        string? architectureLoopPrompt,
        out RunRequest? request)
    {
        request = null;
        if (args.Length < 3 || !IsWorkspaceMode(args[2]))
        {
            return false;
        }

        request = new RunRequest(
            TaskPrompt: DEFAULT_ARCH_LOOP_TASK_PROMPT,
            WorkspacePath: args[1],
            WorkspaceMode: args[2],
            Workflow: "architecture-loop",
            ProjectName: args.Length >= 5 ? args[4] : null,
            ModelOverrides: args.Length >= 6 ? ParseOverrides(args[5]) : null,
            BuildCommand: args.Length >= 7 ? args[6] : null,
            ArchitectureLoopMode: true,
            ArchitectureLoopPrompt: architectureLoopPrompt);

        return true;
    }

    private static bool TryParseStandardShape(
        string[] args,
        string? architectureLoopPrompt,
        bool forceLoopWorkflow,
        out RunRequest? request)
    {
        request = null;
        if (args.Length < 4 || !IsWorkspaceMode(args[3]))
        {
            return false;
        }

        string workflow = forceLoopWorkflow
            ? "architecture-loop"
            : (args.Length >= 5 ? args[4] : "frontend_feature");

        request = new RunRequest(
            TaskPrompt: ResolveTaskPrompt(args[1], forceLoopWorkflow),
            WorkspacePath: args[2],
            WorkspaceMode: args[3],
            Workflow: workflow,
            ProjectName: args.Length >= 6 ? args[5] : null,
            ModelOverrides: args.Length >= 7 ? ParseOverrides(args[6]) : null,
            BuildCommand: args.Length >= 8 ? args[7] : null,
            ArchitectureLoopMode: forceLoopWorkflow,
            ArchitectureLoopPrompt: architectureLoopPrompt);

        return true;
    }

    private static bool IsWorkspaceMode(string? value)
        => value is not null
        && (value.Equals("existing-folder", StringComparison.OrdinalIgnoreCase)
            || value.Equals("new-project", StringComparison.OrdinalIgnoreCase)
            || value.Equals("existing-git", StringComparison.OrdinalIgnoreCase));

    private static string ResolveTaskPrompt(string? inputTaskPrompt, bool architectureLoopMode)
    {
        if (!architectureLoopMode)
        {
            return inputTaskPrompt ?? string.Empty;
        }

        return string.IsNullOrWhiteSpace(inputTaskPrompt)
            ? DEFAULT_ARCH_LOOP_TASK_PROMPT
            : inputTaskPrompt;
    }

    /// <summary>
    /// Parses a comma-separated key=value override string into a dictionary.
    /// </summary>
    /// <param name="overrideText">The override text to parse.</param>
    /// <returns>A dictionary of overrides, or null if empty.</returns>
    internal static IDictionary<string, string>? ParseOverrides(string? overrideText)
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

    /// <summary>
    /// Normalizes an architecture loop prompt by trimming whitespace, returning null for blank values.
    /// </summary>
    /// <param name="prompt">The raw prompt text.</param>
    /// <returns>The trimmed prompt, or null.</returns>
    internal static string? NormalizeArchitectureLoopPrompt(string? prompt)
        => string.IsNullOrWhiteSpace(prompt) ? null : prompt.Trim();
}
