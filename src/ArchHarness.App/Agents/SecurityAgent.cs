using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Agents;

/// <summary>
/// Performs security-focused review and remediation using OWASP-oriented guidance and static heuristics.
/// </summary>
public sealed class SecurityAgent : AgentBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SecurityAgent"/> class.
    /// </summary>
    public SecurityAgent(
        ICopilotClient copilotClient,
        IModelResolver modelResolver,
        IAgentToolPolicyProvider toolPolicyProvider,
        IOptions<AgentsOptions> agentsOptions)
        : base(copilotClient, modelResolver, toolPolicyProvider, agentsOptions, "security", Guid.NewGuid().ToString("N"))
    {
    }

    /// <summary>
    /// Performs a security review and returns structured findings.
    /// </summary>
    public async Task<SecurityReview> ReviewAsync(
        SecurityReviewRequest request,
        string? agentId = null,
        string? agentRole = null,
        CancellationToken cancellationToken = default)
    {
        string model = base.ResolveModel(request.ModelOverrides);
        (string languageLabel, string guidelines) = SecurityPromptBuilder.BuildGuidanceContext(
            request.WorkspaceRoot,
            request.FilesTouched,
            request.Diff,
            request.LanguageScope);
        string systemPrompt = SecurityPromptBuilder.BuildSystemPrompt(guidelines, languageLabel);
        string enforcementPrompt = AgentPromptHelper.BuildEnforcementPrompt(
            request.DelegatedPrompt,
            request.WorkspaceRoot,
            request.FilesTouched,
            request.Diff);

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

        return SecurityAnalysisRunner.Analyze(request.Diff, request.WorkspaceRoot, request.FilesTouched, request.LanguageScope);
    }
}