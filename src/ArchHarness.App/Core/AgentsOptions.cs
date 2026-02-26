namespace ArchHarness.App.Core;

public sealed class AgentModelOptions
{
    public string Model { get; set; } = string.Empty;
    public AgentToolOptions Tools { get; set; } = new();
}

public sealed class AgentsOptions
{
    public AgentModelOptions Orchestration { get; set; } = new() { Model = "claude-sonnet-4.6" };
    public AgentModelOptions Frontend { get; set; } = new() { Model = "claude-sonnet-4.6" };
    public AgentModelOptions Builder { get; set; } = new() { Model = "gpt-5.3-codex" };
    public AgentModelOptions Style { get; set; } = new() { Model = "claude-opus-4.6" };
    public AgentModelOptions Architecture { get; set; } = new() { Model = "claude-opus-4.6" };
}
