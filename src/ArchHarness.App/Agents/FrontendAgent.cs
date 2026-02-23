using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Agents;

public sealed class FrontendAgent : AgentBase
{
    public FrontendAgent(ICopilotClient copilotClient, IOptions<AgentsOptions> options)
        : base(copilotClient, options.Value.Frontend.Model) { }

    public Task<string> CreatePlanAsync(string taskPrompt, CancellationToken cancellationToken = default)
        => CopilotClient.CompleteAsync(Model, $"Generate frontend plan: {taskPrompt}", cancellationToken);
}
