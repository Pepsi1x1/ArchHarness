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
        => CopilotClient.CompleteAsync(
            ResolveModel(modelOverrides),
            $"{FrontendInstructions}{Environment.NewLine}{Environment.NewLine}WorkspaceRoot: {workspace.RootPath}{Environment.NewLine}{Environment.NewLine}{delegatedPrompt}",
            cancellationToken);
}
