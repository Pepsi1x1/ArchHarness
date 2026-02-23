using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using ArchHarness.App.Workspace;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Agents;

public sealed class BuilderAgent : AgentBase
{
    public BuilderAgent(ICopilotClient copilotClient, IOptions<AgentsOptions> options)
        : base(copilotClient, options.Value.Builder.Model) { }

    public async Task<IReadOnlyList<string>> ImplementAsync(IWorkspaceAdapter workspace, string objective, IReadOnlyList<string>? requiredActions = null, CancellationToken cancellationToken = default)
    {
        _ = await CopilotClient.CompleteAsync(Model, $"Implement objective: {objective}", cancellationToken);
        if (requiredActions is { Count: > 0 })
        {
            await workspace.WriteTextAsync("ARCHITECTURE_ACTIONS.md", string.Join(Environment.NewLine, requiredActions), cancellationToken);
            return new[] { "ARCHITECTURE_ACTIONS.md" };
        }

        await workspace.WriteTextAsync("IMPLEMENTATION_NOTE.md", $"Implemented: {objective}", cancellationToken);
        return new[] { "IMPLEMENTATION_NOTE.md" };
    }
}
