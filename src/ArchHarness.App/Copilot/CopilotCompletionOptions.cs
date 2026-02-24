namespace ArchHarness.App.Copilot;

public enum CopilotSystemMessageMode
{
    Append,
    Replace
}

public sealed class CopilotCompletionOptions
{
    public string? SystemMessage { get; init; }
    public CopilotSystemMessageMode SystemMessageMode { get; init; } = CopilotSystemMessageMode.Append;
    public IReadOnlyList<string>? AvailableTools { get; init; }
    public IReadOnlyList<string>? ExcludedTools { get; init; }
}
