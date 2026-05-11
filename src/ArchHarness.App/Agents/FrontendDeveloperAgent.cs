using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using ArchHarness.App.Workspace;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Agents;

/// <summary>
/// Frontend Developer agent responsible for implementing UI/UX changes in the workspace.
/// </summary>
public sealed class FrontendDeveloperAgent : AgentBase
{
    private static readonly SearchOption RECURSIVE = SearchOption.AllDirectories;

    /// <summary>
    /// Initializes a new instance of the <see cref="FrontendDeveloperAgent"/> class.
    /// </summary>
    /// <param name="copilotClient">The Copilot client for model completions.</param>
    /// <param name="modelResolver">Resolves which model to use for this agent.</param>
    /// <param name="toolPolicyProvider">Provides tool access policies for the agent.</param>
    /// <param name="agentsOptions">Configuration options for agent behavior.</param>
    public FrontendDeveloperAgent(ICopilotClient copilotClient, IModelResolver modelResolver, IAgentToolPolicyProvider toolPolicyProvider, IOptions<AgentsOptions> agentsOptions)
        : base(copilotClient, modelResolver, toolPolicyProvider, agentsOptions, "frontend-developer", Guid.NewGuid().ToString("N")) { }

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
        string guidelines = base.IsGuidelinesDisabled ? string.Empty : LoadFrontendGuidelines(workspace.RootPath, delegatedPrompt);
        string systemPrompt = BuildSystemPrompt(guidelines, base.IsGuidelinesDisabled);
        string promptTemplate = PromptLoader.Load("Frontend Developer", "execution.md");
        string prompt = PromptLoader.Render(
            promptTemplate,
            ("{{WorkspaceRoot}}", workspace.RootPath),
            ("{{DelegatedPrompt}}", delegatedPrompt));

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

    private static string BuildSystemPrompt(string guidelines, bool disableGuidelines)
    {
        string systemInstructions = PromptLoader.Load("Frontend Developer", "system.md");
        if (disableGuidelines)
        {
            return systemInstructions;
        }

        return $"""
            {systemInstructions}

            Apply the following frontend guidelines:
            {guidelines}
            """;
    }

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

        AddIf(output, hasVue, "frontend-developer-agent-vue3.md");
        AddIf(output, hasBlazor, "frontend-developer-agent-dotnet-blazor.md");
        AddIf(output, hasTypeScript, "frontend-developer-agent-typescript.md");
        AddIf(output, hasJavaScript, "frontend-developer-agent-javascript.md");

        // Avoid defaulting to generic HTML/CSS guidance for dotnet workspaces unless explicitly requested.
        bool hasHtmlCss = explicitHtmlCssPrompt || (!hasDotnet && hasHtmlCssFiles);
        AddIf(output, hasHtmlCss, "frontend-developer-agent-html-css.md");

        if (output.Count == 0)
        {
            output.Add(hasDotnet ? "frontend-developer-agent-dotnet-blazor.md" : "frontend-developer-agent-html-css.md");
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
        => GuidelineLoader.Load("Frontend Developer", fileName, $"No guideline file found for {fileName}. Apply strong frontend architecture and accessibility standards.");
}
