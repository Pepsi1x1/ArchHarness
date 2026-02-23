using ArchHarness.App.Core;
using GitHub.Copilot.SDK;

namespace ArchHarness.App.Copilot;

internal static class CopilotClientOptionsFactory
{
    public static CopilotClientOptions Build(CopilotOptions options, bool autoRestart)
    {
        var clientOptions = new CopilotClientOptions
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

        if (!string.IsNullOrWhiteSpace(options.CliPath))
        {
            clientOptions.CliPath = options.CliPath;
        }

        if (!string.IsNullOrWhiteSpace(options.CliUrl))
        {
            clientOptions.CliUrl = options.CliUrl;
        }

        if (options.CliArgs.Count > 0)
        {
            clientOptions.CliArgs = options.CliArgs.ToArray();
        }

        var token = Environment.GetEnvironmentVariable(options.ApiTokenEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(token))
        {
            clientOptions.GithubToken = token;
            clientOptions.UseLoggedInUser = false;
        }

        return clientOptions;
    }
}
