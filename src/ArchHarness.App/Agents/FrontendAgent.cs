using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using ArchHarness.App.Workspace;

namespace ArchHarness.App.Agents;

public sealed class FrontendAgent : AgentBase
{
    private const string FrontendInstructions = """
        You are the Frontend Agent.
        Execute delegated frontend tasks using agent-mode built-in tools.
        Focus on UI/UX design, component architecture, accessibility, and state management decisions.
        Create and edit frontend-related files directly within the workspace.
        Return a concise completion summary.
        """;

    public FrontendAgent(ICopilotClient copilotClient, IModelResolver modelResolver)
        : base(copilotClient, modelResolver, "frontend") { }

    public Task<string> CreatePlanAsync(
        IWorkspaceAdapter workspace,
        string delegatedPrompt,
        IDictionary<string, string>? modelOverrides,
        CancellationToken cancellationToken = default)
    {
        var guidelines = LoadFrontendGuidelines(workspace.RootPath, delegatedPrompt);
        var prompt = $"""
            {FrontendInstructions}

            Apply the following frontend guidelines:
            {guidelines}

            WorkspaceRoot: {workspace.RootPath}

            DelegatedPrompt:
            {delegatedPrompt}
            """;

        return CopilotClient.CompleteAsync(ResolveModel(modelOverrides), prompt, cancellationToken);
    }

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

        var hasVue = Directory.GetFiles(workspaceRoot, "*.vue", SearchOption.AllDirectories).Length > 0
            || File.Exists(Path.Combine(workspaceRoot, "package.json"))
            || prompt.Contains("vue");
        if (hasVue)
        {
            output.Add("frontend-builder-agent-vue3.md");
        }

        var hasBlazor = Directory.GetFiles(workspaceRoot, "*.razor", SearchOption.AllDirectories).Length > 0
            || prompt.Contains("blazor");
        if (hasBlazor)
        {
            output.Add("frontend-builder-agent-dotnet-blazor.md");
        }

        var hasTypeScript = Directory.GetFiles(workspaceRoot, "*.ts", SearchOption.AllDirectories).Length > 0
            || Directory.GetFiles(workspaceRoot, "*.tsx", SearchOption.AllDirectories).Length > 0
            || prompt.Contains("typescript")
            || prompt.Contains(".ts");
        if (hasTypeScript)
        {
            output.Add("frontend-builder-agent-typescript.md");
        }

        var hasJavaScript = Directory.GetFiles(workspaceRoot, "*.js", SearchOption.AllDirectories).Length > 0
            || Directory.GetFiles(workspaceRoot, "*.jsx", SearchOption.AllDirectories).Length > 0
            || prompt.Contains("javascript")
            || prompt.Contains(".js");
        if (hasJavaScript)
        {
            output.Add("frontend-builder-agent-javascript.md");
        }

        var hasHtmlCss = Directory.GetFiles(workspaceRoot, "*.html", SearchOption.AllDirectories).Length > 0
            || Directory.GetFiles(workspaceRoot, "*.css", SearchOption.AllDirectories).Length > 0
            || prompt.Contains("html")
            || prompt.Contains("css");
        if (hasHtmlCss)
        {
            output.Add("frontend-builder-agent-html-css.md");
        }

        if (output.Count == 0)
        {
            output.Add("frontend-builder-agent-html-css.md");
        }

        return output.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
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
}
