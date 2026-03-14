using ArchHarness.App.Core;
using GitHub.Copilot.SDK;

namespace ArchHarness.App.Copilot;

/// <summary>
/// Builds Copilot SDK client options from application configuration.
/// </summary>
internal static class CopilotClientOptionsFactory
{
    /// <summary>
    /// Creates a <see cref="CopilotClientOptions"/> instance from the provided application options.
    /// </summary>
    /// <param name="options">The Copilot configuration options.</param>
    /// <param name="autoRestart">Whether the SDK should auto-restart on failure.</param>
    /// <returns>A configured SDK client options instance.</returns>
    public static CopilotClientOptions Build(CopilotOptions options, bool autoRestart)
    {
        CopilotClientOptions clientOptions = new CopilotClientOptions
        {
            AutoStart = true,
            AutoRestart = autoRestart,
            Cwd = Directory.GetCurrentDirectory(),
            UseStdio = options.UseStdio,
            LogLevel = options.LogLevel
        };

        if (options.Port > 0)
        {
            clientOptions.Port = options.Port;
        }

        clientOptions.CliPath = !string.IsNullOrWhiteSpace(options.CliPath)
            ? options.CliPath
            : "copilot";

        if (!string.IsNullOrWhiteSpace(options.CliUrl))
        {
            clientOptions.CliUrl = options.CliUrl;
        }

        if (options.CliArgs.Count > 0)
        {
            clientOptions.CliArgs = options.CliArgs.ToArray();
        }

        string? token = Environment.GetEnvironmentVariable(options.ApiTokenEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(token))
        {
            clientOptions.GitHubToken = token;
            clientOptions.UseLoggedInUser = false;
        }

        return clientOptions;
    }
}
