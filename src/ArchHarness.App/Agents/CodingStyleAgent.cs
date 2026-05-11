using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Agents;

/// <summary>
/// Agent responsible for enforcing coding style, naming conventions, and language-specific standards.
/// </summary>
public sealed class CodingStyleAgent : AgentBase
{

    /// <summary>
    /// Initializes a new instance of <see cref="CodingStyleAgent"/>.
    /// </summary>
    /// <param name="copilotClient">The Copilot client for completions.</param>
    /// <param name="modelResolver">The model resolver.</param>
    /// <param name="toolPolicyProvider">The tool policy provider.</param>
    /// <param name="agentsOptions">The agents configuration options.</param>
    public CodingStyleAgent(ICopilotClient copilotClient, IModelResolver modelResolver, IAgentToolPolicyProvider toolPolicyProvider, IOptions<AgentsOptions> agentsOptions)
        : base(copilotClient, modelResolver, toolPolicyProvider, agentsOptions, "coding-style", Guid.NewGuid().ToString("N"))
    {
    }

    /// <summary>
    /// Enforces coding style standards on the workspace by running a Copilot completion with the style guidelines.
    /// </summary>
    /// <param name="request">The style enforcement request containing workspace context and scope.</param>
    /// <param name="agentId">Optional override for the agent identifier.</param>
    /// <param name="agentRole">Optional override for the agent role.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task EnforceAsync(
        StyleEnforcementRequest request,
        string? agentId = null,
        string? agentRole = null,
        CancellationToken cancellationToken = default)
    {
        string model = base.ResolveModel(request.ModelOverrides);
        (string languageLabel, string guidelines) = BuildGuidanceContext(request.WorkspaceRoot, request.FilesTouched, request.Diff, request.LanguageScope);
        string systemPrompt = BuildSystemPrompt(guidelines, languageLabel);
        string enforcementPrompt = AgentPromptHelper.BuildEnforcementPrompt(request.DelegatedPrompt, request.WorkspaceRoot, request.FilesTouched, request.Diff);
        CopilotCompletionOptions options = base.ApplyToolPolicy(new CopilotCompletionOptions
        {
            SystemMessage = systemPrompt,
            SystemMessageMode = CopilotSystemMessageMode.Append
        });

        _ = await base.CopilotClient.CompleteAsync(
            model,
            enforcementPrompt,
            options,
            agentId: agentId ?? base.Id,
            agentRole: agentRole ?? base.Role,
            cancellationToken);
    }

    private static (string LanguageLabel, string Guidelines) BuildGuidanceContext(
        string workspaceRoot,
        IReadOnlyList<string> filesTouched,
        string diff,
        IReadOnlyList<string>? languageScope)
    {
        return AgentPromptHelper.BuildGuidanceContext(
            workspaceRoot, filesTouched, diff, languageScope,
            AgentPromptHelper.ReviewGuidelineKind.CodingStyle);
    }

    private static string BuildSystemPrompt(string guidelines, string languageLabel)
    {
        string systemInstructions = PromptLoader.Load("CodingStyle", "system.md");

        return $"""
            {systemInstructions}

            LanguageContext: {languageLabel}
            Apply the following coding style guidelines for this language:
            {guidelines}
            """;
    }
}
