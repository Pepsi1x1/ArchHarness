namespace ArchHarness.App.Agents;

/// <summary>
/// Shared prompt-building and language-resolution utilities used by review agents.
/// </summary>
internal static class AgentPromptHelper
{
    private const string REVIEW_ENFORCEMENT_PROMPT_FALLBACK = """
        WorkspaceRoot: {{WorkspaceRoot}}
        Write boundaries: You may modify any file or directory under WorkspaceRoot; do not read or write paths outside WorkspaceRoot.

        DelegatedPrompt:
        {{DelegatedPrompt}}

        FilesTouched: {{FilesTouched}}
        CurrentDiffSnapshot:
        {{CurrentDiffSnapshot}}
        """;

    /// <summary>
    /// Builds the enforcement prompt sent to review agents (CodingStyle, Security, and Architecture).
    /// </summary>
    /// <param name="delegatedPrompt">The delegated task prompt.</param>
    /// <param name="workspaceRoot">The root path of the workspace.</param>
    /// <param name="filesTouched">Files modified during the build phase.</param>
    /// <param name="diff">The current diff snapshot.</param>
    /// <returns>A formatted enforcement prompt string.</returns>
    public static string BuildEnforcementPrompt(
        string delegatedPrompt,
        string workspaceRoot,
        IReadOnlyList<string> filesTouched,
        string diff)
    {
        string touched = filesTouched.Count == 0 ? "(none)" : string.Join(", ", filesTouched);
        string diffPreview = diff.Length <= 4000 ? diff : diff[..4000];
        string promptTemplate = PromptLoader.Load("Shared", "review-enforcement.md", REVIEW_ENFORCEMENT_PROMPT_FALLBACK);

        return PromptLoader.Render(
            promptTemplate,
            ("{{WorkspaceRoot}}", workspaceRoot),
            ("{{DelegatedPrompt}}", delegatedPrompt),
            ("{{FilesTouched}}", touched),
            ("{{CurrentDiffSnapshot}}", diffPreview));
    }

    /// <summary>
    /// Resolves the language label and guideline text for a review agent using a per-language file-name mapper.
    /// Consolidates the duplicated BuildGuidanceContext pattern from individual prompt builders.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root path.</param>
    /// <param name="filesTouched">Files modified during the run.</param>
    /// <param name="diff">The current diff snapshot.</param>
    /// <param name="languageScope">Optional explicit language scope.</param>
    /// <param name="guidelineSubfolder">The Guidelines subfolder name (e.g. "Architecture Review", "Security", "CodingStyle").</param>
    /// <param name="sectionTitlePrefix">Prefix for the section title (e.g. "GUIDELINES", "SECURITY GUIDELINES").</param>
    /// <param name="fileNameMapper">Maps a language identifier to the guideline file name.</param>
    /// <param name="fallbackMessage">Fallback text when a guideline file is not found.</param>
    /// <returns>A tuple of the language label and concatenated guidelines text.</returns>
    public static (string LanguageLabel, string Guidelines) BuildGuidanceContext(
        string workspaceRoot,
        IReadOnlyList<string> filesTouched,
        string diff,
        IReadOnlyList<string>? languageScope,
        string guidelineSubfolder,
        string sectionTitlePrefix,
        Func<string, string> fileNameMapper,
        string fallbackMessage)
    {
        IReadOnlyList<string> languages = ResolveLanguages(workspaceRoot, filesTouched, diff, languageScope);
        string languageLabel = string.Join(", ", languages);
        string guidelines = LoadGuidelinesForLanguages(languages, guidelineSubfolder, sectionTitlePrefix, fileNameMapper, fallbackMessage);
        return (languageLabel, guidelines);
    }

    /// <summary>
    /// Loads and concatenates guideline sections for the given languages.
    /// </summary>
    private static string LoadGuidelinesForLanguages(
        IReadOnlyList<string> languages,
        string guidelineSubfolder,
        string sectionTitlePrefix,
        Func<string, string> fileNameMapper,
        string fallbackMessage)
    {
        List<string> sections = new List<string>();
        foreach (string language in languages)
        {
            string fileName = fileNameMapper(language);
            string text = GuidelineLoader.Load(guidelineSubfolder, fileName, fallbackMessage);
            sections.Add($"=== {language.ToUpperInvariant()} {sectionTitlePrefix} ==={Environment.NewLine}{text}");
        }

        return string.Join(Environment.NewLine + Environment.NewLine, sections);
    }

    /// <summary>
    /// Resolves the language scope for review agents based on workspace contents, touched files,
    /// diff content, and explicit scope overrides.
    /// </summary>
    /// <param name="workspaceRoot">The root path of the workspace.</param>
    /// <param name="filesTouched">Files modified during the build phase.</param>
    /// <param name="diff">The current diff snapshot.</param>
    /// <param name="languageScope">Explicit language scope override, if any.</param>
    /// <returns>A list of detected or specified language identifiers.</returns>
    public static IReadOnlyList<string> ResolveLanguages(
        string workspaceRoot,
        IReadOnlyList<string> filesTouched,
        string diff,
        IReadOnlyList<string>? languageScope)
    {
        if (languageScope is { Count: > 0 })
        {
            return languageScope
                .Select(x => x.Trim().ToLowerInvariant())
                .Where(x => x is "dotnet" or "vue3")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        static bool LooksLikeVueFile(string path)
            => path.EndsWith(".vue", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase);

        List<string> output = new List<string>();

        if (filesTouched.Any(LooksLikeVueFile) || diff.Contains(".vue", StringComparison.OrdinalIgnoreCase))
        {
            output.Add("vue3");
        }

        bool hasCsproj = Directory.GetFiles(workspaceRoot, "*.csproj", SearchOption.AllDirectories).Length > 0;
        bool hasCs = Directory.GetFiles(workspaceRoot, "*.cs", SearchOption.AllDirectories).Length > 0;
        if (hasCsproj || hasCs || filesTouched.Any(x => x.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            output.Add("dotnet");
        }

        bool hasPackageJson = File.Exists(Path.Combine(workspaceRoot, "package.json"));
        if (hasPackageJson)
        {
            output.Add("vue3");
        }

        if (output.Count == 0)
        {
            output.Add("dotnet");
        }

        return output.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
