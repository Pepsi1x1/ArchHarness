using ArchHarness.App.Copilot;
using ArchHarness.App.Core;

namespace ArchHarness.App.Agents;

public sealed class StyleAgent : AgentBase
{
    private const string StyleInstructions = """
        You are the Coding Style Agent.
        Enforce coding style, naming conventions, and language-specific coding standards by directly editing files.
        Run in agent mode and use built-in tools to apply required style and standards fixes.
        Keep changes inside WorkspaceRoot and avoid changing behavior unless required by style compliance.
        Return a concise completion summary after applying changes.
        """;

    public StyleAgent(ICopilotClient copilotClient, IModelResolver modelResolver, IAgentToolPolicyProvider toolPolicyProvider)
        : base(copilotClient, modelResolver, toolPolicyProvider, "style", Guid.NewGuid().ToString("N"))
    {
    }

    public async Task EnforceAsync(
        StyleEnforcementRequest request,
        string? agentId = null,
        string? agentRole = null,
        CancellationToken cancellationToken = default)
    {
        string model = this.ResolveModel(request.ModelOverrides);
        (string languageLabel, string guidelines) = BuildGuidanceContext(request.WorkspaceRoot, request.FilesTouched, request.Diff, request.LanguageScope);
        string systemPrompt = BuildSystemPrompt(guidelines, languageLabel);
        var enforcementPrompt = AgentPromptHelper.BuildEnforcementPrompt(request.DelegatedPrompt, request.WorkspaceRoot, request.FilesTouched, request.Diff);
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
            {StyleInstructions}

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
        => GuidelineLoader.Load("Style", fileName, "No style guideline file found. Apply strict naming, readability, and language coding standards.");
}
