using ArchHarness.App.Core;
using ArchHarness.App.Copilot;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Agents;

/// <summary>
/// Base class for all agents, providing model resolution, tool policy enforcement, and common configuration.
/// </summary>
public abstract class AgentBase
{
    /// <summary>The Copilot client used for completions.</summary>
    protected readonly ICopilotClient CopilotClient;
    private readonly IModelResolver _modelResolver;
    private readonly IAgentToolPolicyProvider _toolPolicyProvider;
    private readonly IOptions<AgentsOptions> _agentsOptions;

    /// <summary>Gets the unique identifier for this agent instance.</summary>
    public string Id { get; }

    /// <summary>Gets the role identifier for this agent (e.g., "frontend-developer", "backend-developer").</summary>
    public string Role { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="AgentBase"/>.
    /// </summary>
    /// <param name="copilotClient">The Copilot client for completions.</param>
    /// <param name="modelResolver">The model resolver for determining which model to use.</param>
    /// <param name="toolPolicyProvider">The tool policy provider for enforcing tool access.</param>
    /// <param name="agentsOptions">The agents configuration options.</param>
    /// <param name="role">The role identifier for this agent.</param>
    /// <param name="id">The unique identifier for this agent instance.</param>
    protected AgentBase(
        ICopilotClient copilotClient,
        IModelResolver modelResolver,
        IAgentToolPolicyProvider toolPolicyProvider,
        IOptions<AgentsOptions> agentsOptions,
        string role,
        string id)
    {
        this.CopilotClient = copilotClient;
        this._modelResolver = modelResolver;
        this._toolPolicyProvider = toolPolicyProvider;
        this._agentsOptions = agentsOptions;
        this.Id = id;
        this.Role = role;
    }

    /// <summary>Gets whether guideline loading is disabled for this agent role.</summary>
    protected bool IsGuidelinesDisabled => this._agentsOptions.Value.ForRole(this.Role).DisableGuidelines;

    /// <summary>Gets the configured options for this agent role.</summary>
    protected AgentModelOptions AgentOptions => this._agentsOptions.Value.ForRole(this.Role);

    /// <summary>Gets the default model for this agent's role.</summary>
    public string DefaultModel => this._modelResolver.Resolve(this.Role, overrides: null);

    /// <summary>
    /// Resolves the model for this agent's role, applying any overrides.
    /// </summary>
    /// <param name="overrides">Optional model overrides keyed by role.</param>
    /// <returns>The resolved model identifier.</returns>
    public string ResolveModel(IDictionary<string, string>? overrides)
        => this._modelResolver.Resolve(this.Role, overrides);

    /// <summary>
    /// Merges the configured tool policy with the provided completion options.
    /// </summary>
    /// <param name="options">The base completion options to augment with tool policies.</param>
    /// <returns>A new options instance with merged tool lists.</returns>
    protected CopilotCompletionOptions ApplyToolPolicy(CopilotCompletionOptions options)
    {
        AgentToolPolicy policy = this._toolPolicyProvider.Resolve(this.Role);
        IReadOnlyList<string>? available = MergeTools(policy.AvailableTools, options.AvailableTools);
        IReadOnlyList<string>? excluded = MergeTools(policy.ExcludedTools, options.ExcludedTools);

        return new CopilotCompletionOptions
        {
            SystemMessage = options.SystemMessage,
            SystemMessageMode = options.SystemMessageMode,
            ReasoningEffort = options.ReasoningEffort ?? this._modelResolver.ResolveReasoningEffort(this.Role),
            AvailableTools = available,
            ExcludedTools = excluded
        };
    }

    private static IReadOnlyList<string>? MergeTools(IReadOnlyList<string> primary, IReadOnlyList<string>? secondary)
    {
        string[] merged = primary
            .Concat(secondary ?? Array.Empty<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return merged.Length == 0 ? null : merged;
    }
}
