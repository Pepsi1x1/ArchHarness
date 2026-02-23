using System.ComponentModel;
using System.Diagnostics;
using ArchHarness.App.Core;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Copilot;

public interface IStartupPreflightValidator
{
    Task<PreflightValidationResult> ValidateAsync(CancellationToken cancellationToken = default);
}

public sealed record PreflightValidationResult(bool IsSuccess, string Summary, IReadOnlyList<string> FixSteps);

public sealed class CopilotStartupPreflightValidator : IStartupPreflightValidator
{
    private readonly CopilotOptions _options;
    private readonly IDiscoveredModelCatalog _catalog;

    public CopilotStartupPreflightValidator(IOptions<CopilotOptions> options, IDiscoveredModelCatalog catalog)
    {
        _options = options.Value;
        _catalog = catalog;
    }

    public async Task<PreflightValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        var cliCheck = await CheckCliAsync();
        if (!cliCheck.IsSuccess)
        {
            return cliCheck;
        }

        var authCheck = await CheckAuthenticationAsync();
        if (!authCheck.IsSuccess)
        {
            return authCheck;
        }

        return new PreflightValidationResult(true, "Preflight passed: Copilot CLI is available and authentication is valid.", Array.Empty<string>());
    }

    private static async Task<PreflightValidationResult> CheckCliAsync()
    {
        try
        {
            var info = new ProcessStartInfo("copilot", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = info };
            process.Start();
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                return new PreflightValidationResult(true, string.IsNullOrWhiteSpace(stdout) ? "copilot --version succeeded." : stdout.Trim(), Array.Empty<string>());
            }

            return new PreflightValidationResult(
                false,
                "Copilot CLI is installed but could not be executed successfully.",
                new[]
                {
                    "Run `copilot --version` in your terminal and resolve any local CLI errors.",
                    "Reinstall or update Copilot CLI: https://docs.github.com/copilot/how-tos/set-up/install-copilot-cli",
                    $"CLI stderr: {stderr.Trim()}"
                });
        }
        catch (Win32Exception)
        {
            return new PreflightValidationResult(
                false,
                "Copilot CLI was not found on PATH.",
                new[]
                {
                    "Install Copilot CLI: https://docs.github.com/copilot/how-tos/set-up/install-copilot-cli",
                    "Ensure `copilot` is available on PATH and restart your terminal/session.",
                    "Verify installation with `copilot --version`."
                });
        }
    }

    private async Task<PreflightValidationResult> CheckAuthenticationAsync()
    {
        var token = Environment.GetEnvironmentVariable(_options.ApiTokenEnvironmentVariable);
        var clientOptions = CopilotClientOptionsFactory.Build(_options, autoRestart: false);

        try
        {
            await using var client = new GitHub.Copilot.SDK.CopilotClient(clientOptions);
            await client.StartAsync();
            await client.PingAsync("archharness-preflight");
            await RefreshDiscoveredModelsWithAuthGuardAsync(client);
            return new PreflightValidationResult(true, "Copilot SDK ping succeeded.", Array.Empty<string>());
        }
        catch (Exception ex)
        {
            var fixSteps = new List<string>
            {
                "Run `copilot` to open the Copilot CLI interactive session.",
                "At the prompt, run `/login` and complete authentication in the browser.",
                "After login completes, rerun ArchHarness."
            };

            if (!string.IsNullOrWhiteSpace(token))
            {
                fixSteps.Add($"Validate `{_options.ApiTokenEnvironmentVariable}` is set to a valid token with Copilot access.");
            }
            else
            {
                fixSteps.Add($"Optionally set `{_options.ApiTokenEnvironmentVariable}` to provide token-based auth.");
            }

            if (LooksLikeAuthenticationFailure(ex))
            {
                fixSteps.Insert(0, "Copilot SDK reported an authentication failure (including models.list checks).");
            }

            fixSteps.Add($"Underlying error: {ex.Message}");

            return new PreflightValidationResult(
                false,
                "Copilot SDK failed authentication/connection preflight.",
                fixSteps);
        }
    }

    private async Task RefreshDiscoveredModelsWithAuthGuardAsync(GitHub.Copilot.SDK.CopilotClient client)
    {
        try
        {
            var discovered = await client.ListModelsAsync();
            var names = discovered
                .Select(m =>
                    m.GetType().GetProperty("Id")?.GetValue(m)?.ToString()
                    ?? m.GetType().GetProperty("Name")?.GetValue(m)?.ToString()
                    ?? m.ToString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (names.Length > 0)
            {
                _catalog.ReplaceModels(names!);
            }
            else
            {
                _catalog.ReplaceModels(_options.SupportedModels);
            }
        }
        catch (Exception ex)
        {
            if (LooksLikeAuthenticationFailure(ex))
            {
                throw new InvalidOperationException(
                    "Communication error with Copilot CLI during models.list: not authenticated. Please authenticate first.",
                    ex);
            }

            _catalog.ReplaceModels(_options.SupportedModels);
        }
    }

    private static bool LooksLikeAuthenticationFailure(Exception ex)
    {
        var text = ex.ToString();
        return text.Contains("not authenticated", StringComparison.OrdinalIgnoreCase)
            || text.Contains("authenticate first", StringComparison.OrdinalIgnoreCase)
            || text.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)
            || text.Contains("auth", StringComparison.OrdinalIgnoreCase) && text.Contains("failed", StringComparison.OrdinalIgnoreCase);
    }
}
