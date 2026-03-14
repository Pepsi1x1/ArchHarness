namespace ArchHarness.App.Agents;

/// <summary>
/// Constructs system and enforcement prompts for security reviews, including OWASP-focused guideline loading.
/// </summary>
internal static class SecurityPromptBuilder
{
    private const string SECURITY_INSTRUCTIONS_FALLBACK = """
        You are the Security Agent.
        Enforce secure coding practices and remediate OWASP Top 10 risks by directly editing files.
        Run in agent mode and use built-in tools to make required security fixes.
        Keep changes inside WorkspaceRoot and preserve intended behavior while eliminating vulnerabilities.
        Return a concise completion summary after applying changes.
        """;

    /// <summary>
    /// Builds the full system prompt including security instructions, language label, and guidelines.
    /// </summary>
    /// <param name="guidelines">The concatenated guideline text for all detected languages.</param>
    /// <param name="languageLabel">Comma-separated language identifiers.</param>
    /// <returns>The complete system prompt.</returns>
    public static string BuildSystemPrompt(string guidelines, string languageLabel)
    {
        string systemInstructions = PromptLoader.Load("Security", "system.md", SECURITY_INSTRUCTIONS_FALLBACK);

        return $"""
            {systemInstructions}

            LanguageContext: {languageLabel}
            Apply the following security guidelines for this language:
            {guidelines}
            """;
    }

    /// <summary>
    /// Resolves the language label and matching guideline text for the given workspace context.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root path.</param>
    /// <param name="filesTouched">Files modified during the run.</param>
    /// <param name="diff">The current diff snapshot.</param>
    /// <param name="languageScope">Optional explicit language scope.</param>
    /// <returns>A tuple of the language label and guidelines text.</returns>
    public static (string LanguageLabel, string Guidelines) BuildGuidanceContext(
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

    private static string LoadGuidelinesForLanguages(IReadOnlyList<string> languages)
    {
        List<string> sections = new List<string>();
        foreach (string language in languages)
        {
            string fileName = language.Equals("vue3", StringComparison.OrdinalIgnoreCase)
                ? "vue3-security-review-agent.md"
                : "dotnet-security-review-agent.md";

            string text = GuidelineLoader.Load("Security", fileName, "No security guideline file found. Review against OWASP Top 10 and remediate vulnerabilities directly.");
            sections.Add($"=== {language.ToUpperInvariant()} SECURITY GUIDELINES ==={Environment.NewLine}{text}");
        }

        return string.Join(Environment.NewLine + Environment.NewLine, sections);
    }
}