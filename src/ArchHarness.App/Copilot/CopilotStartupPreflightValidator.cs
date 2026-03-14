using System.ComponentModel;
using System.Diagnostics;
using ArchHarness.App.Core;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Copilot;

/// <summary>
/// Validates that prerequisites for Copilot SDK usage are met (git, CLI, authentication).
/// </summary>
public interface IStartupPreflightValidator
{
    /// <summary>
    /// Runs all preflight checks and returns the aggregated result.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The preflight validation result.</returns>
    Task<PreflightValidationResult> ValidateAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents the outcome of a preflight validation check.
/// </summary>
/// <param name="IsSuccess">Whether the check passed.</param>
/// <param name="Summary">A human-readable summary of the result.</param>
/// <param name="FixSteps">Suggested remediation steps if the check failed.</param>
public sealed record PreflightValidationResult(bool IsSuccess, string Summary, IReadOnlyList<string> FixSteps);

/// <summary>
/// Default implementation of <see cref="IStartupPreflightValidator"/> that checks git, Copilot CLI, and authentication.
/// </summary>
public sealed class CopilotStartupPreflightValidator : IStartupPreflightValidator
{
    private readonly CopilotOptions _options;
    private readonly IDiscoveredModelCatalog _catalog;

    /// <summary>
    /// Initializes a new instance of <see cref="CopilotStartupPreflightValidator"/>.
    /// </summary>
    /// <param name="options">The Copilot configuration options.</param>
    /// <param name="catalog">The discovered model catalog for runtime model updates.</param>
    public CopilotStartupPreflightValidator(IOptions<CopilotOptions> options, IDiscoveredModelCatalog catalog)
    {
        this._options = options.Value;
        this._catalog = catalog;
    }

    /// <inheritdoc />
    public async Task<PreflightValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        PreflightValidationResult gitCheck = await CheckGitAsync();
        if (!gitCheck.IsSuccess)
        {
            return gitCheck;
        }

        PreflightValidationResult cliCheck = await CheckCliAsync();
        if (!cliCheck.IsSuccess)
        {
            return cliCheck;
        }

        PreflightValidationResult authCheck = await this.CheckAuthenticationAsync();
        if (!authCheck.IsSuccess)
        {
            return authCheck;
        }

        return new PreflightValidationResult(true, "Preflight passed: git and Copilot CLI are available and authentication is valid.", Array.Empty<string>());
    }

    private static async Task<PreflightValidationResult> CheckGitAsync()
    {
        try
        {
            ProcessStartInfo info = new ProcessStartInfo("git", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using Process process = new Process { StartInfo = info };
            process.Start();
            string stdout = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                return new PreflightValidationResult(
                    true,
                    string.IsNullOrWhiteSpace(stdout) ? "git --version succeeded." : stdout.Trim(),
                    Array.Empty<string>());
            }

            return new PreflightValidationResult(
                false,
                "Git is installed but could not be executed successfully.",
                new[]
                {
                    "Run `git --version` in your terminal and resolve any local git errors.",
                    "Ensure the git executable is available on PATH for the current session.",
                    $"git stderr: {stderr.Trim()}"
                });
        }
        catch (Win32Exception)
        {
            return new PreflightValidationResult(
                false,
                "Git was not found on PATH.",
                new[]
                {
                    "Install git from https://git-scm.com/downloads.",
                    "Ensure `git` is available on PATH and restart your terminal/session.",
                    "Verify installation with `git --version`."
                });
        }
    }

    private static async Task<PreflightValidationResult> CheckCliAsync()
    {
        try
        {
            ProcessStartInfo info = new ProcessStartInfo("copilot", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using Process process = new Process { StartInfo = info };
            process.Start();
            string stdout = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
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
        string? token = Environment.GetEnvironmentVariable(this._options.ApiTokenEnvironmentVariable);
        CopilotClientOptions clientOptions = CopilotClientOptionsFactory.Build(this._options, autoRestart: false);

        try
        {
            await using GitHub.Copilot.SDK.CopilotClient client = new GitHub.Copilot.SDK.CopilotClient(clientOptions);
            await client.StartAsync();
            await client.PingAsync("archharness-preflight");
            await RefreshDiscoveredModelsWithAuthGuardAsync(client);
            return new PreflightValidationResult(true, "Copilot SDK ping succeeded.", Array.Empty<string>());
        }
        catch (Exception ex)
        {
            List<string> fixSteps = new List<string>
            {
                "Run `copilot` to open the Copilot CLI interactive session.",
                "At the prompt, run `/login` and complete authentication in the browser.",
                "After login completes, rerun ArchHarness."
            };

            if (!string.IsNullOrWhiteSpace(token))
            {
                fixSteps.Add($"Validate `{this._options.ApiTokenEnvironmentVariable}` is set to a valid token with Copilot access.");
            }
            else
            {
                fixSteps.Add($"Optionally set `{this._options.ApiTokenEnvironmentVariable}` to provide token-based auth.");
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
                this._catalog.ReplaceModels(names!);
            }
            else
            {
                this._catalog.ReplaceModels(this._options.SupportedModels);
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

            this._catalog.ReplaceModels(this._options.SupportedModels);
        }
    }

    private static bool LooksLikeAuthenticationFailure(Exception ex)
    {
        string text = ex.ToString();
        return text.Contains("not authenticated", StringComparison.OrdinalIgnoreCase)
            || text.Contains("authenticate first", StringComparison.OrdinalIgnoreCase)
            || text.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)
            || text.Contains("auth", StringComparison.OrdinalIgnoreCase) && text.Contains("failed", StringComparison.OrdinalIgnoreCase);
    }
}
