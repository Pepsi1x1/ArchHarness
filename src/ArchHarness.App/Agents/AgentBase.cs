using ArchHarness.App.Core;
using ArchHarness.App.Copilot;

namespace ArchHarness.App.Agents;

public abstract class AgentBase
{
    protected readonly ICopilotClient CopilotClient;
    private readonly IModelResolver _modelResolver;
    private readonly IAgentToolPolicyProvider _toolPolicyProvider;
    public string Id { get; }
    public string Role { get; }

    protected AgentBase(
        ICopilotClient copilotClient,
        IModelResolver modelResolver,
        IAgentToolPolicyProvider toolPolicyProvider,
        string role,
        string id)
    {
        CopilotClient = copilotClient;
        _modelResolver = modelResolver;
        _toolPolicyProvider = toolPolicyProvider;
        Id = id;
        Role = role;
    }

    public string DefaultModel => _modelResolver.Resolve(Role, overrides: null);

    public string ResolveModel(IDictionary<string, string>? overrides)
        => _modelResolver.Resolve(Role, overrides);

    protected CopilotCompletionOptions ApplyToolPolicy(CopilotCompletionOptions options)
    {
        var policy = _toolPolicyProvider.Resolve(Role);
        var available = MergeTools(policy.AvailableTools, options.AvailableTools);
        var excluded = MergeTools(policy.ExcludedTools, options.ExcludedTools);

        return new CopilotCompletionOptions
        {
            SystemMessage = options.SystemMessage,
            SystemMessageMode = options.SystemMessageMode,
            AvailableTools = available,
            ExcludedTools = excluded
        };
    }

    private static IReadOnlyList<string>? MergeTools(IReadOnlyList<string> primary, IReadOnlyList<string>? secondary)
    {
        var merged = primary
            .Concat(secondary ?? Array.Empty<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return merged.Length == 0 ? null : merged;
    }
}
