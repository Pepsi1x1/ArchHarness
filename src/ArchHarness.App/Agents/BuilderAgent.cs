using ArchHarness.App.Copilot;
using ArchHarness.App.Workspace;

namespace ArchHarness.App.Agents;

public sealed class BuilderAgent
{
    private readonly CopilotClient _copilotClient;
    private readonly string _model;

    public BuilderAgent(CopilotClient copilotClient, string model)
    {
        _copilotClient = copilotClient;
        _model = model;
    }

    public async Task<IReadOnlyList<string>> ImplementAsync(IWorkspaceAdapter workspace, string objective, IReadOnlyList<string>? requiredActions = null, CancellationToken cancellationToken = default)
    {
        _ = await _copilotClient.CompleteAsync(_model, $"Implement objective: {objective}", cancellationToken);
        if (requiredActions is { Count: > 0 })
        {
            await workspace.WriteTextAsync("ARCHITECTURE_ACTIONS.md", string.Join(Environment.NewLine, requiredActions), cancellationToken);
            return new[] { "ARCHITECTURE_ACTIONS.md" };
        }

        await workspace.WriteTextAsync("IMPLEMENTATION_NOTE.md", $"Implemented: {objective}", cancellationToken);
        return new[] { "IMPLEMENTATION_NOTE.md" };
    }
}
