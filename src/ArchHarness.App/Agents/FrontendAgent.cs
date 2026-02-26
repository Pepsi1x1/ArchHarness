using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using ArchHarness.App.Workspace;

namespace ArchHarness.App.Agents;

/// <summary>
/// Frontend agent responsible for implementing UI/UX changes in the workspace.
/// </summary>
public sealed class FrontendAgent : AgentBase
{
    private static readonly SearchOption RECURSIVE = SearchOption.AllDirectories;
    private const string FRONTEND_INSTRUCTIONS = """
        You are the Frontend Agent.
        Execute delegated frontend tasks using agent-mode built-in tools.
        Focus on UI/UX design, component architecture, accessibility, and state management decisions.
        Create and edit frontend-related files directly within the workspace.
        Return a concise completion summary.
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="FrontendAgent"/> class.
    /// </summary>
    /// <param name="copilotClient">Client for Copilot completions.</param>
    /// <param name="modelResolver">Resolver for model identifiers.</param>
    /// <param name="toolPolicyProvider">Provider for agent tool access policies.</param>
    public FrontendAgent(ICopilotClient copilotClient, IModelResolver modelResolver, IAgentToolPolicyProvider toolPolicyProvider)
        : base(copilotClient, modelResolver, toolPolicyProvider, "frontend", Guid.NewGuid().ToString("N")) { }

    /// <summary>
    /// Implements frontend changes in the workspace based on the given delegated prompt.
    /// </summary>
    /// <param name="workspace">The workspace adapter for file operations.</param>
    /// <param name="delegatedPrompt">The prompt describing what frontend work to perform.</param>
    /// <param name="modelOverrides">Optional model override mappings.</param>
    /// <param name="agentId">Optional agent identifier override.</param>
    /// <param name="agentRole">Optional agent role override.</param>
    /// <param name="cancellationToken">Token to signal cancellation.</param>
    /// <returns>A list of files that were created or modified.</returns>
    public async Task<IReadOnlyList<string>> ImplementAsync(
        IWorkspaceAdapter workspace,
        string delegatedPrompt,
        IDictionary<string, string>? modelOverrides,
        string? agentId = null,
        string? agentRole = null,
        CancellationToken cancellationToken = default)
    {
        Dictionary<string, (long Length, long LastWriteUtcTicks)> baseline = WorkspaceSnapshotHelper.CaptureSnapshot(workspace.RootPath);
        string guidelines = LoadFrontendGuidelines(workspace.RootPath, delegatedPrompt);
        string systemPrompt = BuildSystemPrompt(guidelines);
        string prompt = $"""
            WorkspaceRoot: {workspace.RootPath}

            DelegatedPrompt:
            {delegatedPrompt}

            Return a concise completion summary.
            """;

        CopilotCompletionOptions options = base.ApplyToolPolicy(new CopilotCompletionOptions
        {
            SystemMessage = systemPrompt,
            SystemMessageMode = CopilotSystemMessageMode.Append
        });

        _ = await base.CopilotClient.CompleteAsync(
            base.ResolveModel(modelOverrides),
            prompt,
            options,
            agentId: agentId ?? base.Id,
            agentRole: agentRole ?? base.Role,
            cancellationToken);

        return WorkspaceSnapshotHelper.DetectChanges(workspace.RootPath, baseline);
    }

    private static string BuildSystemPrompt(string guidelines)
        => $"""
            {FRONTEND_INSTRUCTIONS}

            Apply the following frontend guidelines:
            {guidelines}
            """;

    private static string LoadFrontendGuidelines(string workspaceRoot, string delegatedPrompt)
    {
        IReadOnlyList<string> selected = ResolveFrontendGuidelineFiles(workspaceRoot, delegatedPrompt);
        List<string> sections = new List<string>();
        foreach (string fileName in selected)
        {
            string text = TryLoadGuidelineFile(fileName);
            sections.Add($"=== {fileName} ==={Environment.NewLine}{text}");
        }

        return string.Join(Environment.NewLine + Environment.NewLine, sections);
    }

    private static IReadOnlyList<string> ResolveFrontendGuidelineFiles(string workspaceRoot, string delegatedPrompt)
    {
        List<string> output = new List<string>();
        string prompt = delegatedPrompt.ToLowerInvariant();
        bool hasDotnet = HasAnyFiles(workspaceRoot, "*.csproj", "*.cs");
        bool hasVue = HasAnyFiles(workspaceRoot, "*.vue")
            || File.Exists(Path.Combine(workspaceRoot, "package.json"))
            || prompt.Contains("vue", StringComparison.Ordinal);
        bool hasBlazor = HasAnyFiles(workspaceRoot, "*.razor")
            || prompt.Contains("blazor", StringComparison.Ordinal);
        bool hasTypeScript = HasAnyFiles(workspaceRoot, "*.ts", "*.tsx")
            || prompt.Contains("typescript", StringComparison.Ordinal)
            || prompt.Contains(".ts", StringComparison.Ordinal);
        bool hasJavaScript = HasAnyFiles(workspaceRoot, "*.js", "*.jsx")
            || prompt.Contains("javascript", StringComparison.Ordinal)
            || prompt.Contains(".js", StringComparison.Ordinal);
        bool explicitHtmlCssPrompt = prompt.Contains("html", StringComparison.Ordinal) || prompt.Contains("css", StringComparison.Ordinal);
        bool hasHtmlCssFiles = HasAnyFiles(workspaceRoot, "*.html", "*.css");

        AddIf(output, hasVue, "frontend-builder-agent-vue3.md");
        AddIf(output, hasBlazor, "frontend-builder-agent-dotnet-blazor.md");
        AddIf(output, hasTypeScript, "frontend-builder-agent-typescript.md");
        AddIf(output, hasJavaScript, "frontend-builder-agent-javascript.md");

        // Avoid defaulting to generic HTML/CSS guidance for dotnet workspaces unless explicitly requested.
        bool hasHtmlCss = explicitHtmlCssPrompt || (!hasDotnet && hasHtmlCssFiles);
        AddIf(output, hasHtmlCss, "frontend-builder-agent-html-css.md");

        if (output.Count == 0)
        {
            output.Add(hasDotnet ? "frontend-builder-agent-dotnet-blazor.md" : "frontend-builder-agent-html-css.md");
        }

        return output.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool HasAnyFiles(string workspaceRoot, params string[] patterns)
        => patterns.Any(pattern => Directory.GetFiles(workspaceRoot, pattern, RECURSIVE).Length > 0);

    private static void AddIf(ICollection<string> output, bool condition, string value)
    {
        if (condition)
        {
            output.Add(value);
        }
    }

    private static string TryLoadGuidelineFile(string fileName)
        => GuidelineLoader.Load("Frontend", fileName, $"No guideline file found for {fileName}. Apply strong frontend architecture and accessibility standards.");
}
