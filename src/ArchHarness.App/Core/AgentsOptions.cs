namespace ArchHarness.App.Core;

public sealed class AgentModelOptions
{
    public string Model { get; set; } = string.Empty;
}

public sealed class AgentsOptions
{
    public AgentModelOptions Orchestration { get; set; } = new() { Model = "sonnet-4.6" };
    public AgentModelOptions Frontend { get; set; } = new() { Model = "sonnet-4.6" };
    public AgentModelOptions Builder { get; set; } = new() { Model = "codex-5.3" };
    public AgentModelOptions Architecture { get; set; } = new() { Model = "opus-4.6" };
}
