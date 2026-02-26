using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using ArchHarness.App.Workspace;

namespace ArchHarness.App.Agents;

public sealed class BuilderAgent : AgentBase
{
    private const string BuilderInstructions = """
        You are the Builder/Implementation Agent.
        Execute the delegated prompt using agent-mode built-in tools.
        Create and edit workspace files directly where required.
        Add or update tests when applicable.
        Return a concise completion summary and list key changed files.
        """;

    public BuilderAgent(ICopilotClient copilotClient, IModelResolver modelResolver, IAgentToolPolicyProvider toolPolicyProvider)
        : base(copilotClient, modelResolver, toolPolicyProvider, "builder", Guid.NewGuid().ToString("N")) { }

    public async Task<IReadOnlyList<string>> ImplementAsync(
        IWorkspaceAdapter workspace,
        string objective,
        IDictionary<string, string>? modelOverrides,
        IReadOnlyList<string>? requiredActions = null,
        string? agentId = null,
        string? agentRole = null,
        CancellationToken cancellationToken = default)
    {
        var touched = new List<string>();
        var model = ResolveModel(modelOverrides);
        var baseline = CaptureWorkspaceSnapshot(workspace.RootPath);

        if (requiredActions is { Count: > 0 })
        {
            await workspace.WriteTextAsync("ARCHITECTURE_ACTIONS.md", string.Join(Environment.NewLine, requiredActions), cancellationToken);
            touched.Add("ARCHITECTURE_ACTIONS.md");
        }

        var generationPrompt = BuildGenerationPrompt(workspace, objective, requiredActions);
        var systemPrompt = BuildSystemPrompt();
        var options = ApplyToolPolicy(new CopilotCompletionOptions
        {
            SystemMessage = systemPrompt,
            SystemMessageMode = CopilotSystemMessageMode.Append
        });

        _ = await CopilotClient.CompleteAsync(
            model,
            generationPrompt,
            options,
            agentId: agentId ?? this.Id,
            agentRole: agentRole ?? this.Role,
            cancellationToken);

        var changedFiles = CaptureChangedFilesSinceBaseline(workspace.RootPath, baseline);
        touched.AddRange(changedFiles);

        return touched.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static Dictionary<string, (long Length, long LastWriteUtcTicks)> CaptureWorkspaceSnapshot(string workspaceRoot)
    {
        var snapshot = new Dictionary<string, (long Length, long LastWriteUtcTicks)>(StringComparer.OrdinalIgnoreCase);
        foreach (var fullPath in Directory.GetFiles(workspaceRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(workspaceRoot, fullPath);
            if (ShouldIgnorePath(relativePath))
            {
                continue;
            }

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

        foreach ((var path, var signature) in current)
        {
            if (!baseline.TryGetValue(path, out var baselineSignature) || baselineSignature != signature)
            {
                changed.Add(path);
            }
        }

        foreach (var baselinePath in baseline.Keys)
        {
            if (!current.ContainsKey(baselinePath))
            {
                changed.Add(baselinePath);
            }
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

    private static string BuildGenerationPrompt(IWorkspaceAdapter workspace, string objective, IReadOnlyList<string>? requiredActions)
    {
        var requiredActionsSection = requiredActions is { Count: > 0 }
            ? $"{Environment.NewLine}RequiredActions:{Environment.NewLine}{string.Join(" | ", requiredActions)}"
            : string.Empty;

        return $"""
            WorkspaceRoot: {workspace.RootPath}
            Write boundaries: You may modify any file or directory contained in WorkspaceRoot; do not read or write paths outside WorkspaceRoot.
            Execution mode: use built-in file and terminal tools as needed.

            DelegatedPrompt:
            {objective}
            {requiredActionsSection}
            """;
    }

    private static string BuildSystemPrompt()
    {
        var builderGuidelines = LoadBuilderGuidelines();
        return $"""
            {BuilderInstructions}

            Apply the following builder guidelines:
            {builderGuidelines}
            """;
    }

    private static string LoadBuilderGuidelines()
    {
        const string fileName = "backend-builder-agent.md";
        var searchRoots = new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() };
        foreach (var root in searchRoots)
        {
            var path = Path.Combine(root, "Guidelines", "Builder", fileName);
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }

        return "No builder guideline file found. Follow strict backend implementation standards and add tests.";
    }
}
