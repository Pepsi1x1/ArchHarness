namespace ArchHarness.App.Agents;

/// <summary>
/// Constructs system and enforcement prompts for architecture reviews, including guideline loading.
/// </summary>
internal static class ArchitecturePromptBuilder
{
    private const string ArchitectureInstructions = """
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
        => $"""
            {ArchitectureInstructions}

            LanguageContext: {languageLabel}
            Apply the following architecture guidelines for this language:
            {guidelines}
            """;

    /// <summary>
    /// Builds the enforcement prompt sent as the user message for architecture review.
    /// </summary>
    /// <param name="delegatedPrompt">The delegated prompt from the orchestrator.</param>
    /// <param name="workspaceRoot">The workspace root path.</param>
    /// <param name="filesTouched">Files modified during the run.</param>
    /// <param name="diff">The current diff snapshot.</param>
    /// <returns>The enforcement prompt text.</returns>
    public static string BuildEnforcementPrompt(
        string delegatedPrompt,
        string workspaceRoot,
        IReadOnlyList<string> filesTouched,
        string diff)
    {
        string touched = filesTouched.Count == 0 ? "(none)" : string.Join(", ", filesTouched);
        string diffPreview = diff.Length <= 4000 ? diff : diff[..4000];

        return $"""
            WorkspaceRoot: {workspaceRoot}
            Write boundaries: You may modify any file or directory under WorkspaceRoot; do not read or write paths outside WorkspaceRoot.

            DelegatedPrompt:
            {delegatedPrompt}

            FilesTouched: {touched}
            CurrentDiffSnapshot:
            {diffPreview}
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
        IReadOnlyList<string> languages = ResolveLanguages(workspaceRoot, filesTouched, diff, languageScope);
        string languageLabel = string.Join(", ", languages);
        string guidelines = LoadGuidelinesForLanguages(languages);
        return (languageLabel, guidelines);
    }

    private static IReadOnlyList<string> ResolveLanguages(
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

    private static string LoadGuidelinesForLanguages(IReadOnlyList<string> languages)
    {
        List<string> sections = new List<string>();
        foreach (string language in languages)
        {
            string fileName = language.Equals("vue3", StringComparison.OrdinalIgnoreCase)
                ? "vue3-architecture-review-agent.md"
                : "dotnet-architecture-review-agent.md";

            string text = TryLoadGuidelineFile(fileName);
            sections.Add($"=== {language.ToUpperInvariant()} GUIDELINES ==={Environment.NewLine}{text}");
        }

        return string.Join(Environment.NewLine + Environment.NewLine, sections);
    }

    private static string TryLoadGuidelineFile(string fileName)
    {
        string[] searchRoots = new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() };
        foreach (string root in searchRoots)
        {
            string path = Path.Combine(root, "Guidelines", "Architecture Review", fileName);
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }

        return "No guideline file found. Apply strict SOLID/DRY review and enforce architecture consistency.";
    }
}
