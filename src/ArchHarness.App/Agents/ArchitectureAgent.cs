using ArchHarness.App.Copilot;
using ArchHarness.App.Core;

namespace ArchHarness.App.Agents;

/// <summary>
/// Coordinates architecture reviews by delegating prompt construction to ArchitecturePromptBuilder
/// and static analysis to AnalysisRunner.
/// </summary>
public sealed class ArchitectureAgent : AgentBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ArchitectureAgent"/> class.
    /// </summary>
    /// <param name="copilotClient">Client for Copilot completions.</param>
    /// <param name="modelResolver">Resolver for model selection.</param>
    /// <param name="toolPolicyProvider">Provider for agent tool policies.</param>
    /// <param name="agentsOptions">Agent configuration options.</param>
    public ArchitectureAgent(ICopilotClient copilotClient, IModelResolver modelResolver, IAgentToolPolicyProvider toolPolicyProvider, Microsoft.Extensions.Options.IOptions<AgentsOptions> agentsOptions)
        : base(copilotClient, modelResolver, toolPolicyProvider, agentsOptions, "architecture", Guid.NewGuid().ToString("N")) { }

    /// <summary>
    /// Performs an architecture review by sending prompts to Copilot and running static analyzers.
    /// </summary>
    /// <param name="request">The architecture review request.</param>
    /// <param name="agentId">Optional agent identifier override.</param>
    /// <param name="agentRole">Optional agent role override.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The aggregated architecture review.</returns>
    public async Task<ArchitectureReview> ReviewAsync(
        ArchitectureReviewRequest request,
        string? agentId = null,
        string? agentRole = null,
        CancellationToken cancellationToken = default)
    {
        string model = ResolveModel(request.ModelOverrides);
        (string languageLabel, string guidelines) = ArchitecturePromptBuilder.BuildGuidanceContext(
            request.WorkspaceRoot, request.FilesTouched, request.Diff, request.LanguageScope);
        string systemPrompt = ArchitecturePromptBuilder.BuildSystemPrompt(guidelines, languageLabel);
        string enforcementPrompt = ArchitecturePromptBuilder.BuildEnforcementPrompt(
            request.DelegatedPrompt, request.WorkspaceRoot, request.FilesTouched, request.Diff);

        CopilotCompletionOptions options = ApplyToolPolicy(new CopilotCompletionOptions
        {
            SystemMessage = systemPrompt,
            SystemMessageMode = CopilotSystemMessageMode.Append
        });

        _ = await CopilotClient.CompleteAsync(
            model,
            enforcementPrompt,
            options,
            agentId: agentId ?? this.Id,
            agentRole: agentRole ?? this.Role,
            cancellationToken);

        return AnalysisRunner.Analyze(request.Diff, request.WorkspaceRoot, request.FilesTouched);
    }
}
