using ArchHarness.App.Core;
using ArchHarness.App.Copilot;

namespace ArchHarness.App.Agents;

public abstract class AgentBase
{
    protected readonly ICopilotClient CopilotClient;
    private readonly IModelResolver _modelResolver;
    public string Role { get; }

    protected AgentBase(ICopilotClient copilotClient, IModelResolver modelResolver, string role)
    {
        CopilotClient = copilotClient;
        _modelResolver = modelResolver;
        Role = role;
    }

    public string DefaultModel => _modelResolver.Resolve(Role, overrides: null);

    public string ResolveModel(IDictionary<string, string>? overrides)
        => _modelResolver.Resolve(Role, overrides);
}
