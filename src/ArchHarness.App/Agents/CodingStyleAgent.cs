using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Agents;

/// <summary>
/// Agent responsible for enforcing coding style, naming conventions, and language-specific standards.
/// </summary>
public sealed class CodingStyleAgent : AgentBase
{
    private const string CODING_STYLE_INSTRUCTIONS = """
        You are the Coding Style Agent.
        Enforce coding style, naming conventions, and language-specific coding standards by directly editing files.
        Run in agent mode and use built-in tools to apply required style and standards fixes.
        Keep changes inside WorkspaceRoot and avoid changing behavior unless required by style compliance.
        Return a concise completion summary after applying changes.
        """;

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
        string model = this.ResolveModel(request.ModelOverrides);
        (string languageLabel, string guidelines) = BuildGuidanceContext(request.WorkspaceRoot, request.FilesTouched, request.Diff, request.LanguageScope);
        string systemPrompt = BuildSystemPrompt(guidelines, languageLabel);
        string enforcementPrompt = AgentPromptHelper.BuildEnforcementPrompt(request.DelegatedPrompt, request.WorkspaceRoot, request.FilesTouched, request.Diff);
        CopilotCompletionOptions options = this.ApplyToolPolicy(new CopilotCompletionOptions
        {
            SystemMessage = systemPrompt,
            SystemMessageMode = CopilotSystemMessageMode.Append
        });

        _ = await this.CopilotClient.CompleteAsync(
            model,
            enforcementPrompt,
            options,
            agentId: agentId ?? this.Id,
            agentRole: agentRole ?? this.Role,
            cancellationToken);
    }

    private static (string LanguageLabel, string Guidelines) BuildGuidanceContext(
        string workspaceRoot,
        IReadOnlyList<string> filesTouched,
        string diff,
        IReadOnlyList<string>? languageScope)
    {
        IReadOnlyList<string> languages = AgentPromptHelper.ResolveLanguages(workspaceRoot, filesTouched, diff, languageScope);
        string languageLabel = string.Join(", ", languages);
        string guidelines = LoadGuidelinesForLanguages(languages);
        return (languageLabel, guidelines);
    }

    private static string BuildSystemPrompt(string guidelines, string languageLabel)
        => $"""
            {CODING_STYLE_INSTRUCTIONS}

            LanguageContext: {languageLabel}
            Apply the following coding style guidelines for this language:
            {guidelines}
            """;

    private static string LoadGuidelinesForLanguages(IReadOnlyList<string> languages)
    {
        List<string> sections = new List<string>();
        foreach (string language in languages)
        {
            string fileName = language.Equals("vue3", StringComparison.OrdinalIgnoreCase)
                ? "vue3-style-review-agent.md"
                : "dotnet-style-review-agent.md";

            string text = TryLoadGuidelineFile(fileName);
            sections.Add($"=== {language.ToUpperInvariant()} STYLE GUIDELINES ==={Environment.NewLine}{text}");
        }

        return string.Join(Environment.NewLine + Environment.NewLine, sections);
    }

    private static string TryLoadGuidelineFile(string fileName)
        => GuidelineLoader.Load("CodingStyle", fileName, "No coding style guideline file found. Apply strict naming, readability, and language coding standards.");
}
