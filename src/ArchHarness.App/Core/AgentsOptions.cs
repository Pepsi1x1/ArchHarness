namespace ArchHarness.App.Core;

/// <summary>
/// Configuration options for a single agent role, including model selection and tool policies.
/// </summary>
public sealed class AgentModelOptions
{
    /// <summary>Gets or sets the model identifier for this agent role.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Gets or sets the tool policy options for this agent role.</summary>
    public AgentToolOptions Tools { get; set; } = new AgentToolOptions();

    /// <summary>Gets or sets whether guideline loading is disabled for this agent role.</summary>
    public bool DisableGuidelines { get; set; }

    /// <summary>Gets or sets whether architecture loop mode is enabled.</summary>
    public bool ArchitectureLoopMode { get; set; }

    /// <summary>Gets or sets the optional architecture loop prompt.</summary>
    public string? ArchitectureLoopPrompt { get; set; }
}

/// <summary>
/// Root configuration for all agent roles, providing per-role model and tool options.
/// </summary>
public sealed class AgentsOptions
{
    /// <summary>Gets or sets the orchestration agent options.</summary>
    public AgentModelOptions Orchestration { get; set; } = new AgentModelOptions() { Model = "claude-sonnet-4.6" };

    /// <summary>Gets or sets the frontend developer agent options.</summary>
    public AgentModelOptions FrontendDeveloper { get; set; } = new AgentModelOptions() { Model = "claude-sonnet-4.6" };

    /// <summary>Gets or sets the backend developer agent options.</summary>
    public AgentModelOptions BackendDeveloper { get; set; } = new AgentModelOptions() { Model = "gpt-5.3-codex" };

    /// <summary>Gets or sets the build agent options.</summary>
    public AgentModelOptions Build { get; set; } = new AgentModelOptions() { Model = "gpt-4.1" };

    /// <summary>Gets or sets the coding style agent options.</summary>
    public AgentModelOptions CodingStyle { get; set; } = new AgentModelOptions() { Model = "claude-opus-4.6" };

    /// <summary>Gets or sets the security agent options.</summary>
    public AgentModelOptions Security { get; set; } = new AgentModelOptions() { Model = "claude-opus-4.6" };

    /// <summary>Gets or sets the architecture agent options.</summary>
    public AgentModelOptions Architecture { get; set; } = new AgentModelOptions() { Model = "claude-opus-4.6" };

    /// <summary>
    /// Returns the agent model options for the specified role.
    /// </summary>
    /// <param name="role">The agent role identifier.</param>
    /// <returns>The matching agent model options.</returns>
    public AgentModelOptions ForRole(string role) => role.ToLowerInvariant() switch
    {
        "frontend-developer" => FrontendDeveloper,
        "backend-developer" => BackendDeveloper,
        "build" => Build,
        "coding-style" => CodingStyle,
        "security" => Security,
        "architecture" => Architecture,
        "orchestration" => Orchestration,
        _ => new AgentModelOptions()
    };
}
