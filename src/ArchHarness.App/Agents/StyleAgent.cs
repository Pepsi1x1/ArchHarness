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
        string enforcementPrompt = BuildEnforcementPrompt(request.DelegatedPrompt, request.WorkspaceRoot, request.FilesTouched, request.Diff);
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

    private static string BuildEnforcementPrompt(
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

    private static (string LanguageLabel, string Guidelines) BuildGuidanceContext(
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

    private static string BuildSystemPrompt(string guidelines, string languageLabel)
        => $"""
            {StyleInstructions}

            LanguageContext: {languageLabel}
            Apply the following coding style guidelines for this language:
            {guidelines}
            """;

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
                ? "vue3-style-review-agent.md"
                : "dotnet-style-review-agent.md";

            string text = TryLoadGuidelineFile(fileName);
            sections.Add($"=== {language.ToUpperInvariant()} STYLE GUIDELINES ==={Environment.NewLine}{text}");
        }

        return string.Join(Environment.NewLine + Environment.NewLine, sections);
    }

    private static string TryLoadGuidelineFile(string fileName)
    {
        string[] searchRoots = new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() };
        foreach (string root in searchRoots)
        {
            string path = Path.Combine(root, "Guidelines", "Style", fileName);
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }

        return "No style guideline file found. Apply strict naming, readability, and language coding standards.";
    }
}
