using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using ArchHarness.App.Workspace;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Agents;

/// <summary>
/// Builder agent responsible for implementing code changes in the workspace.
/// </summary>
public sealed class BuilderAgent : AgentBase
{
    private const string BUILDER_INSTRUCTIONS = """
        You are the Builder/Implementation Agent.
        Execute the delegated prompt using agent-mode built-in tools.
        Create and edit workspace files directly where required.
        Add or update tests when applicable.
        Return a concise completion summary and list key changed files.
        """;

    public BuilderAgent(ICopilotClient copilotClient, IModelResolver modelResolver, IAgentToolPolicyProvider toolPolicyProvider, IOptions<AgentsOptions> agentsOptions)
        : base(copilotClient, modelResolver, toolPolicyProvider, agentsOptions, "builder", Guid.NewGuid().ToString("N")) { }

    /// <summary>
    /// Implements code changes in the workspace based on the given objective and optional required actions.
    /// </summary>
    /// <param name="workspace">The workspace adapter for file operations.</param>
    /// <param name="objective">The delegated prompt describing what to implement.</param>
    /// <param name="modelOverrides">Optional model override mappings.</param>
    /// <param name="requiredActions">Optional architecture review actions to address.</param>
    /// <param name="agentId">Optional agent identifier override.</param>
    /// <param name="agentRole">Optional agent role override.</param>
    /// <param name="cancellationToken">Token to signal cancellation.</param>
    /// <returns>A list of files that were created or modified.</returns>
    public async Task<IReadOnlyList<string>> ImplementAsync(
        IWorkspaceAdapter workspace,
        string objective,
        IDictionary<string, string>? modelOverrides,
        IReadOnlyList<string>? requiredActions = null,
        string? agentId = null,
        string? agentRole = null,
        CancellationToken cancellationToken = default)
    {
        List<string> touched = new List<string>();
        string model = base.ResolveModel(modelOverrides);
        Dictionary<string, (long Length, long LastWriteUtcTicks)> baseline = WorkspaceSnapshotHelper.CaptureSnapshot(workspace.RootPath);

        if (requiredActions is { Count: > 0 })
        {
            await workspace.WriteTextAsync("ARCHITECTURE_ACTIONS.md", string.Join(Environment.NewLine, requiredActions), cancellationToken);
            touched.Add("ARCHITECTURE_ACTIONS.md");
        }

        string generationPrompt = BuildGenerationPrompt(workspace, objective, requiredActions);
        string systemPrompt = BuildSystemPrompt(IsGuidelinesDisabled);
        CopilotCompletionOptions options = base.ApplyToolPolicy(new CopilotCompletionOptions
        {
            SystemMessage = systemPrompt,
            SystemMessageMode = CopilotSystemMessageMode.Append
        });

        _ = await base.CopilotClient.CompleteAsync(
            model,
            generationPrompt,
            options,
            agentId: agentId ?? base.Id,
            agentRole: agentRole ?? base.Role,
            cancellationToken);

        IReadOnlyList<string> changedFiles = WorkspaceSnapshotHelper.DetectChanges(workspace.RootPath, baseline);
        touched.AddRange(changedFiles);

        return touched.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string BuildGenerationPrompt(IWorkspaceAdapter workspace, string objective, IReadOnlyList<string>? requiredActions)
    {
        string requiredActionsSection = requiredActions is { Count: > 0 }
            ? $"{Environment.NewLine}RequiredActions:{Environment.NewLine}{string.Join(" | ", requiredActions)}"
            : string.Empty;

        return $"""
            WorkspaceRoot: {workspace.RootPath}
            Write boundaries: You may modify any file or directory contained in WorkspaceRoot; do not read or write paths outside WorkspaceRoot.
            Execution mode: use built-in file and terminal tools as needed.

            DelegatedPrompt:
            {objective}
            {requiredActionsSection}
            """;
    }

    private static string BuildSystemPrompt(bool disableGuidelines = false)
    {
        string builderGuidelines = LoadBuilderGuidelines();
        if (disableGuidelines)
        {
            return BUILDER_INSTRUCTIONS;
        }

        return $"""
            {BUILDER_INSTRUCTIONS}

            Apply the following builder guidelines:
            {builderGuidelines}
            """;
    }

    private static string LoadBuilderGuidelines()
        => GuidelineLoader.Load("Builder", "backend-builder-agent.md", "No builder guideline file found. Follow strict backend implementation standards and add tests.");
}
