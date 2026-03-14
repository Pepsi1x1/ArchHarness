namespace ArchHarness.App.Core;

/// <summary>
/// Configuration options for the Copilot SDK client, including model defaults, timeouts, and retry policies.
/// </summary>
public sealed class CopilotOptions
{
    /// <summary>Gets or sets the default model for conversation-mode completions.</summary>
    public string ConversationModel { get; set; } = "gpt-5-mini";

    /// <summary>Gets or sets the environment variable name that holds the API token.</summary>
    public string ApiTokenEnvironmentVariable { get; set; } = "GITHUB_COPILOT_TOKEN";

    /// <summary>Gets or sets the integration identifier sent to the Copilot service.</summary>
    public string IntegrationId { get; set; } = "archharness";

    /// <summary>Gets or sets the optional path to the Copilot CLI executable.</summary>
    public string? CliPath { get; set; }

    /// <summary>Gets or sets the optional URL for the Copilot CLI service.</summary>
    public string? CliUrl { get; set; }

    /// <summary>Gets or sets additional CLI arguments passed to the Copilot process.</summary>
    public List<string> CliArgs { get; set; } = new List<string>();

    /// <summary>Gets or sets the port for the Copilot service. Zero means auto-assign.</summary>
    public int Port { get; set; } = 0;

    /// <summary>Gets or sets whether to use stdio transport for the SDK client.</summary>
    public bool UseStdio { get; set; } = true;

    /// <summary>Gets or sets the log level for the Copilot SDK.</summary>
    public string LogLevel { get; set; } = "info";

    /// <summary>Gets or sets whether streaming responses are enabled.</summary>
    public bool StreamingResponses { get; set; } = true;

    /// <summary>Gets or sets the list of explicitly available tools.</summary>
    public List<string> AvailableTools { get; set; } = new List<string>();

    /// <summary>Gets or sets the list of globally excluded tools.</summary>
    public List<string> ExcludedTools { get; set; } = new List<string>();

    /// <summary>Gets or sets the maximum prompt character count before truncation.</summary>
    public int MaxPromptCharacters { get; set; } = 12000;

    /// <summary>Gets or sets the maximum completion character count before truncation.</summary>
    public int MaxCompletionCharacters { get; set; } = 16000;

    /// <summary>Gets or sets the inactivity timeout in seconds between SDK events. Zero disables the timeout.</summary>
    public int SessionResponseTimeoutSeconds { get; set; } = 0;

    /// <summary>Gets or sets the hard upper bound in seconds for an individual request regardless of event activity. Zero disables the timeout.</summary>
    public int SessionAbsoluteTimeoutSeconds { get; set; } = 900;

    /// <summary>Gets or sets the maximum number of retry attempts for transient errors.</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>Gets or sets the base retry delay in milliseconds for exponential backoff.</summary>
    public int BaseRetryDelayMilliseconds { get; set; } = 250;

}
