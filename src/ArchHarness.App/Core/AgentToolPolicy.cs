using Microsoft.Extensions.Options;

namespace ArchHarness.App.Core;

/// <summary>
/// Configuration options for an agent's allowed and excluded tool lists.
/// </summary>
public sealed class AgentToolOptions
{
    /// <summary>Gets or sets the list of tools explicitly available to the agent.</summary>
    public List<string> AvailableTools { get; set; } = new List<string>();

    /// <summary>Gets or sets the list of tools explicitly excluded from the agent.</summary>
    public List<string> ExcludedTools { get; set; } = new List<string>();
}

/// <summary>
/// Immutable resolved tool policy containing the final available and excluded tool lists.
/// </summary>
/// <param name="AvailableTools">The resolved list of available tools.</param>
/// <param name="ExcludedTools">The resolved list of excluded tools.</param>
public sealed record AgentToolPolicy(
    IReadOnlyList<string> AvailableTools,
    IReadOnlyList<string> ExcludedTools);

/// <summary>
/// Resolves the tool policy for a given agent role by merging configuration with defaults.
/// </summary>
public interface IAgentToolPolicyProvider
{
    /// <summary>
    /// Resolves the effective tool policy for the specified agent role.
    /// </summary>
    /// <param name="role">The agent role identifier.</param>
    /// <returns>The resolved tool policy.</returns>
    AgentToolPolicy Resolve(string role);
}

/// <summary>
/// Default implementation of <see cref="IAgentToolPolicyProvider"/> that builds policies from configuration.
/// </summary>
public sealed class AgentToolPolicyProvider : IAgentToolPolicyProvider
{
    private static readonly string[] DefaultOrchestrationExcluded =
    {
        "edit_file"
    };

    private static readonly string[] DefaultBuildExcluded =
    {
        "edit_file"
    };

    private readonly AgentsOptions _options;

    /// <summary>
    /// Initializes a new instance of <see cref="AgentToolPolicyProvider"/>.
    /// </summary>
    /// <param name="options">The agents configuration options.</param>
    public AgentToolPolicyProvider(IOptions<AgentsOptions> options)
    {
        this._options = options.Value;
    }

    /// <inheritdoc />
    public AgentToolPolicy Resolve(string role)
    {
        AgentToolOptions tools = role.ToLowerInvariant() switch
        {
            "frontend-developer" => this._options.FrontendDeveloper.Tools,
            "backend-developer" => this._options.BackendDeveloper.Tools,
            "build" => this._options.Build.Tools,
            "coding-style" => this._options.CodingStyle.Tools,
            "security" => this._options.Security.Tools,
            "architecture" => this._options.Architecture.Tools,
            "orchestration" => this._options.Orchestration.Tools,
            _ => new AgentToolOptions()
        };

        return role.ToLowerInvariant() switch
        {
            "orchestration" => BuildPolicy(tools, Array.Empty<string>(), DefaultOrchestrationExcluded),
            "frontend-developer" => BuildPolicy(tools, Array.Empty<string>(), Array.Empty<string>()),
            "backend-developer" => BuildPolicy(tools, Array.Empty<string>(), Array.Empty<string>()),
            "build" => BuildPolicy(tools, Array.Empty<string>(), DefaultBuildExcluded),
            "coding-style" => BuildPolicy(tools, Array.Empty<string>(), Array.Empty<string>()),
            "security" => BuildPolicy(tools, Array.Empty<string>(), Array.Empty<string>()),
            "architecture" => BuildPolicy(tools, Array.Empty<string>(), Array.Empty<string>()),
            _ => BuildPolicy(tools, Array.Empty<string>(), Array.Empty<string>())
        };
    }

    private static AgentToolPolicy BuildPolicy(AgentToolOptions tools, IReadOnlyList<string> fallbackAllow, IReadOnlyList<string> fallbackExclude)
    {
        IReadOnlyList<string> available = tools.AvailableTools.Count > 0 ? tools.AvailableTools : fallbackAllow;
        IReadOnlyList<string> excluded = tools.ExcludedTools.Count > 0 ? tools.ExcludedTools : fallbackExclude;

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
