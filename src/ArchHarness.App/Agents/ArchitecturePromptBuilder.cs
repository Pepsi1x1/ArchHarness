namespace ArchHarness.App.Agents;

/// <summary>
/// Constructs system and enforcement prompts for architecture reviews, including guideline loading.
/// </summary>
internal static class ArchitecturePromptBuilder
{
    private const string ARCHITECTURE_INSTRUCTIONS_FALLBACK = """
        You are the Architecture Agent.
        Enforce SOLID, structural cohesion, separation of concerns, and DRY by directly editing files.
        Run in agent mode and use built-in tools to make required architecture changes.
        Keep changes inside WorkspaceRoot and update tests when behavior changes.
        Return a concise completion summary after applying changes.
        """;

    /// <summary>
    /// Builds the full system prompt including architecture instructions, language label, and guidelines.
    /// </summary>
    /// <param name="guidelines">The concatenated guideline text for all detected languages.</param>
    /// <param name="languageLabel">Comma-separated language identifiers.</param>
    /// <returns>The complete system prompt.</returns>
    public static string BuildSystemPrompt(string guidelines, string languageLabel)
    {
        string systemInstructions = PromptLoader.Load("Architecture", "system.md", ARCHITECTURE_INSTRUCTIONS_FALLBACK);

        return $"""
            {systemInstructions}

            LanguageContext: {languageLabel}
            Apply the following architecture guidelines for this language:
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
        return AgentPromptHelper.BuildGuidanceContext(
            workspaceRoot, filesTouched, diff, languageScope,
            "Architecture Review",
            "GUIDELINES",
            language => language.Equals("vue3", StringComparison.OrdinalIgnoreCase)
                ? "vue3-architecture-review-agent.md"
                : "dotnet-architecture-review-agent.md",
            "No guideline file found. Apply strict SOLID/DRY review and enforce architecture consistency.");
    }

}
