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

    public BuilderAgent(ICopilotClient copilotClient, IModelResolver modelResolver)
        : base(copilotClient, modelResolver, "builder") { }

    public async Task<IReadOnlyList<string>> ImplementAsync(
        IWorkspaceAdapter workspace,
        string objective,
        IDictionary<string, string>? modelOverrides,
        IReadOnlyList<string>? requiredActions = null,
        CancellationToken cancellationToken = default)
    {
        var touched = new List<string>();
        var model = ResolveModel(modelOverrides);

        if (requiredActions is { Count: > 0 })
        {
            await workspace.WriteTextAsync("ARCHITECTURE_ACTIONS.md", string.Join(Environment.NewLine, requiredActions), cancellationToken);
            touched.Add("ARCHITECTURE_ACTIONS.md");
        }

        var generationPrompt = BuildGenerationPrompt(workspace, objective, requiredActions);
        _ = await CopilotClient.CompleteAsync(model, generationPrompt, cancellationToken);

        return touched.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string BuildGenerationPrompt(IWorkspaceAdapter workspace, string objective, IReadOnlyList<string>? requiredActions)
    {
        var actions = requiredActions is { Count: > 0 }
            ? string.Join(" | ", requiredActions)
            : "none";

        return $"""
            {BuilderInstructions}

            WorkspaceRoot: {workspace.RootPath}
            Write boundaries: Do not modify files outside WorkspaceRoot.
            Execution mode: use built-in file and terminal tools as needed.

            DelegatedPrompt:
            {objective}

            RequiredActions:
            {actions}
            """;
    }
}
