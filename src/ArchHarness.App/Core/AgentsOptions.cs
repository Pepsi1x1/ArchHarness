namespace ArchHarness.App.Core;

public sealed class AgentModelOptions
{
    public string Model { get; set; } = string.Empty;
    public AgentToolOptions Tools { get; set; } = new();
    public bool DisableGuidelines { get; set; }
    public bool ArchitectureLoopMode { get; set; }
    public string? ArchitectureLoopPrompt { get; set; }
}

public sealed class AgentsOptions
{
    public AgentModelOptions Orchestration { get; set; } = new() { Model = "claude-sonnet-4.6" };
    public AgentModelOptions Frontend { get; set; } = new() { Model = "claude-sonnet-4.6" };
    public AgentModelOptions Builder { get; set; } = new() { Model = "gpt-5.3-codex" };
    public AgentModelOptions Style { get; set; } = new() { Model = "claude-opus-4.6" };
    public AgentModelOptions Architecture { get; set; } = new() { Model = "claude-opus-4.6" };

    public AgentModelOptions ForRole(string role) => role.ToLowerInvariant() switch
    {
        "frontend" => Frontend,
        "builder" => Builder,
        "architecture" => Architecture,
        "orchestration" => Orchestration,
        _ => new AgentModelOptions()
    };
}
