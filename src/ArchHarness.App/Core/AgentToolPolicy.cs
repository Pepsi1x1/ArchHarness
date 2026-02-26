using Microsoft.Extensions.Options;

namespace ArchHarness.App.Core;

public sealed class AgentToolOptions
{
    public List<string> AvailableTools { get; set; } = new();
    public List<string> ExcludedTools { get; set; } = new();
}

public sealed record AgentToolPolicy(
    IReadOnlyList<string> AvailableTools,
    IReadOnlyList<string> ExcludedTools);

public interface IAgentToolPolicyProvider
{
    AgentToolPolicy Resolve(string role);
}

public sealed class AgentToolPolicyProvider : IAgentToolPolicyProvider
{
    private static readonly string[] DefaultOrchestrationExcluded =
    {
        "edit_file"
    };

    private readonly AgentsOptions _options;

    public AgentToolPolicyProvider(IOptions<AgentsOptions> options)
    {
        _options = options.Value;
    }

    public AgentToolPolicy Resolve(string role)
    {
        var tools = role.ToLowerInvariant() switch
        {
            "frontend" => _options.Frontend.Tools,
            "builder" => _options.Builder.Tools,
            "style" => _options.Style.Tools,
            "architecture" => _options.Architecture.Tools,
            "orchestration" => _options.Orchestration.Tools,
            _ => new AgentToolOptions()
        };

        return role.ToLowerInvariant() switch
        {
            "orchestration" => BuildPolicy(tools, Array.Empty<string>(), DefaultOrchestrationExcluded),
            "frontend" => BuildPolicy(tools, Array.Empty<string>(), Array.Empty<string>()),
            "builder" => BuildPolicy(tools, Array.Empty<string>(), Array.Empty<string>()),
            "style" => BuildPolicy(tools, Array.Empty<string>(), Array.Empty<string>()),
            "architecture" => BuildPolicy(tools, Array.Empty<string>(), Array.Empty<string>()),
            _ => BuildPolicy(tools, Array.Empty<string>(), Array.Empty<string>())
        };
    }

    private static AgentToolPolicy BuildPolicy(AgentToolOptions tools, IReadOnlyList<string> fallbackAllow, IReadOnlyList<string> fallbackExclude)
    {
        var available = tools.AvailableTools.Count > 0 ? tools.AvailableTools : fallbackAllow;
        var excluded = tools.ExcludedTools.Count > 0 ? tools.ExcludedTools : fallbackExclude;

        return new AgentToolPolicy(
            NormalizeList(available),
            NormalizeList(excluded));
    }

    private static IReadOnlyList<string> NormalizeList(IReadOnlyList<string> input)
    {
        if (input.Count == 0)
        {
            return Array.Empty<string>();
        }

        return input
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
