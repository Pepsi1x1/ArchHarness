using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using ArchHarness.App.Workspace;

namespace ArchHarness.App.Agents;

public sealed class FrontendAgent : AgentBase
{
    private static readonly SearchOption Recursive = SearchOption.AllDirectories;
    private const string FrontendInstructions = """
        You are the Frontend Agent.
        Execute delegated frontend tasks using agent-mode built-in tools.
        Focus on UI/UX design, component architecture, accessibility, and state management decisions.
        Create and edit frontend-related files directly within the workspace.
        Return a concise completion summary.
        """;

    public FrontendAgent(ICopilotClient copilotClient, IModelResolver modelResolver, IAgentToolPolicyProvider toolPolicyProvider)
        : base(copilotClient, modelResolver, toolPolicyProvider, "frontend", Guid.NewGuid().ToString("N")) { }

    public async Task<IReadOnlyList<string>> ImplementAsync(
        IWorkspaceAdapter workspace,
        string delegatedPrompt,
        IDictionary<string, string>? modelOverrides,
        string? agentId = null,
        string? agentRole = null,
        CancellationToken cancellationToken = default)
    {
        var baseline = CaptureWorkspaceSnapshot(workspace.RootPath);
        var guidelines = LoadFrontendGuidelines(workspace.RootPath, delegatedPrompt);
        var systemPrompt = BuildSystemPrompt(guidelines);
        var prompt = $"""
            WorkspaceRoot: {workspace.RootPath}

            DelegatedPrompt:
            {delegatedPrompt}

            Return a concise completion summary.
            """;

        var options = ApplyToolPolicy(new CopilotCompletionOptions
        {
            SystemMessage = systemPrompt,
            SystemMessageMode = CopilotSystemMessageMode.Append
        });

        _ = await CopilotClient.CompleteAsync(
            ResolveModel(modelOverrides),
            prompt,
            options,
            agentId: agentId ?? this.Id,
            agentRole: agentRole ?? this.Role,
            cancellationToken);

        return CaptureChangedFilesSinceBaseline(workspace.RootPath, baseline);
    }

    private static string BuildSystemPrompt(string guidelines)
        => $"""
            {FrontendInstructions}

            Apply the following frontend guidelines:
            {guidelines}
            """;

    private static string LoadFrontendGuidelines(string workspaceRoot, string delegatedPrompt)
    {
        var selected = ResolveFrontendGuidelineFiles(workspaceRoot, delegatedPrompt);
        var sections = new List<string>();
        foreach (var fileName in selected)
        {
            var text = TryLoadGuidelineFile(fileName);
            sections.Add($"=== {fileName} ==={Environment.NewLine}{text}");
        }

        return string.Join(Environment.NewLine + Environment.NewLine, sections);
    }

    private static IReadOnlyList<string> ResolveFrontendGuidelineFiles(string workspaceRoot, string delegatedPrompt)
    {
        var output = new List<string>();
        var prompt = delegatedPrompt.ToLowerInvariant();
        var hasDotnet = HasAnyFiles(workspaceRoot, "*.csproj", "*.cs");
        var hasVue = HasAnyFiles(workspaceRoot, "*.vue")
            || File.Exists(Path.Combine(workspaceRoot, "package.json"))
            || prompt.Contains("vue", StringComparison.Ordinal);
        var hasBlazor = HasAnyFiles(workspaceRoot, "*.razor")
            || prompt.Contains("blazor", StringComparison.Ordinal);
        var hasTypeScript = HasAnyFiles(workspaceRoot, "*.ts", "*.tsx")
            || prompt.Contains("typescript", StringComparison.Ordinal)
            || prompt.Contains(".ts", StringComparison.Ordinal);
        var hasJavaScript = HasAnyFiles(workspaceRoot, "*.js", "*.jsx")
            || prompt.Contains("javascript", StringComparison.Ordinal)
            || prompt.Contains(".js", StringComparison.Ordinal);
        var explicitHtmlCssPrompt = prompt.Contains("html", StringComparison.Ordinal) || prompt.Contains("css", StringComparison.Ordinal);
        var hasHtmlCssFiles = HasAnyFiles(workspaceRoot, "*.html", "*.css");

        AddIf(output, hasVue, "frontend-builder-agent-vue3.md");
        AddIf(output, hasBlazor, "frontend-builder-agent-dotnet-blazor.md");
        AddIf(output, hasTypeScript, "frontend-builder-agent-typescript.md");
        AddIf(output, hasJavaScript, "frontend-builder-agent-javascript.md");

        // Avoid defaulting to generic HTML/CSS guidance for dotnet workspaces unless explicitly requested.
        var hasHtmlCss = explicitHtmlCssPrompt || (!hasDotnet && hasHtmlCssFiles);
        AddIf(output, hasHtmlCss, "frontend-builder-agent-html-css.md");

        if (output.Count == 0)
        {
            output.Add(hasDotnet ? "frontend-builder-agent-dotnet-blazor.md" : "frontend-builder-agent-html-css.md");
        }

        return output.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool HasAnyFiles(string workspaceRoot, params string[] patterns)
        => patterns.Any(pattern => Directory.GetFiles(workspaceRoot, pattern, Recursive).Length > 0);

    private static void AddIf(ICollection<string> output, bool condition, string value)
    {
        if (condition)
        {
            output.Add(value);
        }
    }

    private static string TryLoadGuidelineFile(string fileName)
    {
        var searchRoots = new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() };
        foreach (var root in searchRoots)
        {
            var path = Path.Combine(root, "Guidelines", "Frontend", fileName);
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }

        return $"No guideline file found for {fileName}. Apply strong frontend architecture and accessibility standards.";
    }

    private static Dictionary<string, (long Length, long LastWriteUtcTicks)> CaptureWorkspaceSnapshot(string workspaceRoot)
    {
        var snapshot = new Dictionary<string, (long Length, long LastWriteUtcTicks)>(StringComparer.OrdinalIgnoreCase);
        foreach (var fullPath in Directory
                     .GetFiles(workspaceRoot, "*", SearchOption.AllDirectories)
                     .Where(fullPath => !ShouldIgnorePath(Path.GetRelativePath(workspaceRoot, fullPath))))
        {
            var relativePath = Path.GetRelativePath(workspaceRoot, fullPath);
            var info = new FileInfo(fullPath);
            snapshot[relativePath] = (info.Length, info.LastWriteTimeUtc.Ticks);
        }

        return snapshot;
    }

    private static IReadOnlyList<string> CaptureChangedFilesSinceBaseline(
        string workspaceRoot,
        IReadOnlyDictionary<string, (long Length, long LastWriteUtcTicks)> baseline)
    {
        var current = CaptureWorkspaceSnapshot(workspaceRoot);
        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in current
                     .Where(entry => !baseline.TryGetValue(entry.Key, out var baselineSignature)
                                     || baselineSignature != entry.Value))
        {
            changed.Add(entry.Key);
        }

        foreach (var baselinePath in baseline.Keys.Where(baselinePath => !current.ContainsKey(baselinePath)))
        {
            changed.Add(baselinePath);
        }

        return changed.ToArray();
    }

    private static bool ShouldIgnorePath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        return normalized.StartsWith(".git/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("bin/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("obj/", StringComparison.OrdinalIgnoreCase);
    }
}
