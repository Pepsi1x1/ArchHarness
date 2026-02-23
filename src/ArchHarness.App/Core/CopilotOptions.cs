namespace ArchHarness.App.Core;

public sealed class CopilotOptions
{
    public string ConversationModel { get; set; } = "gpt-5-mini";
    public string ApiTokenEnvironmentVariable { get; set; } = "GITHUB_COPILOT_TOKEN";
    public string IntegrationId { get; set; } = "archharness";
    public string? CliPath { get; set; }
    public string? CliUrl { get; set; }
    public List<string> CliArgs { get; set; } = new();
    public int Port { get; set; } = 0;
    public bool UseStdio { get; set; } = true;
    public string LogLevel { get; set; } = "info";
    public bool StreamingResponses { get; set; } = true;
    public List<string> AvailableTools { get; set; } = new();
    public List<string> ExcludedTools { get; set; } = new();
    public int MaxPromptCharacters { get; set; } = 12000;
    public int MaxCompletionCharacters { get; set; } = 16000;
    // Inactivity timeout between SDK events; set to 0 to disable.
    public int SessionResponseTimeoutSeconds { get; set; } = 0;
    // Hard upper bound for an individual request regardless of event activity; set to 0 to disable.
    public int SessionAbsoluteTimeoutSeconds { get; set; } = 900;
    public int MaxRetries { get; set; } = 2;
    public int BaseRetryDelayMilliseconds { get; set; } = 250;
    public List<string> SupportedModels { get; set; } = new()
    {
        "claude-sonnet-4.6",
        "claude-sonnet-4.5",
        "claude-haiku-4.5",
        "claude-opus-4.6",
        "claude-opus-4.6-fast",
        "claude-opus-4.5",
        "claude-sonnet-4",
        "gemini-3-pro-preview",
        "gpt-5.3-codex",
        "gpt-5.2-codex",
        "gpt-5.2",
        "gpt-5.1-codex-max",
        "gpt-5.1-codex",
        "gpt-5.1",
        "gpt-5.1-codex-mini",
        "gpt-5-mini",
        "gpt-4.1"
    };
}
