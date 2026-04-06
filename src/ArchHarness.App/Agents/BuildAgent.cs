using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using ArchHarness.App.Workspace;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Agents;

/// <summary>
/// Build agent responsible for baseline and intermediate build execution in the workspace.
/// </summary>
public sealed class BuildAgent : AgentBase
{
    private const string BUILD_INSTRUCTIONS_FALLBACK = """
        You are the Build Agent.
        Your role is build execution and build-result triage only.
        Use terminal tools to run the provided build command from WorkspaceRoot.
        Do not edit workspace files or apply code changes.
        Return a concise summary including whether the build passed and the most important failures when it did not.
        """;
    private const string BUILD_EXECUTION_PROMPT_FALLBACK = """
        WorkspaceRoot: {{WorkspaceRoot}}
        BuildCommand: {{BuildCommand}}

        DelegatedPrompt:
        {{Objective}}

        Execute the build-related work directly. If BuildCommand is provided, use it exactly unless the delegated prompt explicitly says otherwise.
        Return a concise completion summary.
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="BuildAgent"/> class.
    /// </summary>
    /// <param name="copilotClient">The Copilot client for model completions.</param>
    /// <param name="modelResolver">Resolves which model to use for this agent.</param>
    /// <param name="toolPolicyProvider">Provides tool access policies for the agent.</param>
    /// <param name="agentsOptions">Configuration options for agent behavior.</param>
    public BuildAgent(ICopilotClient copilotClient, IModelResolver modelResolver, IAgentToolPolicyProvider toolPolicyProvider, IOptions<AgentsOptions> agentsOptions)
        : base(copilotClient, modelResolver, toolPolicyProvider, agentsOptions, "build", Guid.NewGuid().ToString("N")) { }

    /// <summary>
    /// Runs the delegated build task in the workspace.
    /// </summary>
    /// <param name="workspace">The workspace adapter for file operations.</param>
    /// <param name="objective">The delegated prompt describing what build work to perform.</param>
    /// <param name="buildCommand">The build command to execute.</param>
    /// <param name="modelOverrides">Optional model override mappings.</param>
    /// <param name="stepId">The plan step ID producing this build result.</param>
    /// <param name="agentId">Optional agent identifier override.</param>
    /// <param name="agentRole">Optional agent role override.</param>
    /// <param name="cancellationToken">Token to signal cancellation.</param>
    /// <returns>A structured build outcome indicating pass/fail and summary.</returns>
    public async Task<BuildOutcome> RunBuildAsync(
        IWorkspaceAdapter workspace,
        string objective,
        string? buildCommand,
        IDictionary<string, string>? modelOverrides,
        int stepId = 0,
        string? agentId = null,
        string? agentRole = null,
        CancellationToken cancellationToken = default)
    {
        string promptTemplate = PromptLoader.Load("Build", "execution.md", BUILD_EXECUTION_PROMPT_FALLBACK);
        string prompt = PromptLoader.Render(
            promptTemplate,
            ("{{WorkspaceRoot}}", workspace.RootPath),
            ("{{BuildCommand}}", buildCommand ?? "(none)"),
            ("{{Objective}}", objective));
        string systemInstructions = PromptLoader.Load("Build", "system.md", BUILD_INSTRUCTIONS_FALLBACK);

        CopilotCompletionOptions options = base.ApplyToolPolicy(new CopilotCompletionOptions
        {
            SystemMessage = systemInstructions,
            SystemMessageMode = CopilotSystemMessageMode.Append
        });

        string response = await base.CopilotClient.CompleteAsync(
            base.ResolveModel(modelOverrides),
            prompt,
            options,
            agentId: agentId ?? base.Id,
            agentRole: agentRole ?? base.Role,
            cancellationToken);

        bool passed = InferBuildPassed(response);
        string summary = string.IsNullOrWhiteSpace(response) ? "Build step completed." : response.Trim();
        return new BuildOutcome(passed, summary, stepId, DateTimeOffset.UtcNow);
    }

    private static bool InferBuildPassed(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return true;
        }

        string lower = response.ToLowerInvariant();
        if (lower.Contains("build failed") || lower.Contains("build error") || lower.Contains("compilation failed"))
        {
            return false;
        }

        if (lower.Contains("build succeeded") || lower.Contains("build passed") || lower.Contains("build success"))
        {
            return true;
        }

        return true;
    }
}