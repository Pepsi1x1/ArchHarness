using System.Diagnostics;
using System.Reflection;

namespace ArchHarness.App.Core;

public static class ShadowRuntimeBootstrap
{
    private const string ShadowRunFlag = "ARCHHARNESS_SHADOW_RUN";
    private const string ShadowDisableFlag = "ARCHHARNESS_SHADOW_DISABLE";
    private const string ShadowForceFlag = "ARCHHARNESS_SHADOW_FORCE";
    private const string OriginalBaseDirFlag = "ARCHHARNESS_ORIGINAL_BASEDIR";
    private const string ShadowRootFlag = "ARCHHARNESS_SHADOW_ROOT";

    public static bool TryRelaunchFromShadowCopy(string[] args)
    {
        if (string.Equals(Environment.GetEnvironmentVariable(ShadowDisableFlag), "1", StringComparison.Ordinal))
        {
            return false;
        }

        if (string.Equals(Environment.GetEnvironmentVariable(ShadowRunFlag), "1", StringComparison.Ordinal))
        {
            return false;
        }

        // Keep interactive setup sessions in-process by default.
        // Explicit CLI run invocations can still use shadow mode safely.
        bool forceShadow = string.Equals(Environment.GetEnvironmentVariable(ShadowForceFlag), "1", StringComparison.Ordinal);
        bool isExplicitCliRun = args.Length > 0 && string.Equals(args[0], "run", StringComparison.OrdinalIgnoreCase);
        if (!forceShadow && !isExplicitCliRun && IsLikelyInteractiveConsole())
        {
            return false;
        }

        string sourceBaseDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        string shadowRoot = GetShadowRootPath();

        if (sourceBaseDirectory.StartsWith(Path.GetFullPath(shadowRoot), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            CleanupOldShadowRuns(shadowRoot);

            string runDirectory = Path.Combine(
                shadowRoot,
                $"run-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Environment.ProcessId}");
            CopyDirectoryRecursively(sourceBaseDirectory, runDirectory);

            ProcessStartInfo? relaunchStartInfo = BuildRelaunchStartInfo(runDirectory, args);
            if (relaunchStartInfo is null)
            {
                return false;
            }

            relaunchStartInfo.WorkingDirectory = Directory.GetCurrentDirectory();
            relaunchStartInfo.EnvironmentVariables[ShadowRunFlag] = "1";
            relaunchStartInfo.EnvironmentVariables[OriginalBaseDirFlag] = sourceBaseDirectory;

            _ = Process.Start(relaunchStartInfo);
            return true;
        }
        catch
        {
            // If shadow relaunch fails, continue running in-place.
            return false;
        }
    }

    private static ProcessStartInfo? BuildRelaunchStartInfo(string runDirectory, IReadOnlyList<string> args)
    {
        string entryAssemblyName = Assembly.GetEntryAssembly()?.GetName().Name ?? "ArchHarness.App";
        string shadowDllPath = Path.Combine(runDirectory, $"{entryAssemblyName}.dll");
        string renderedArgs = string.Join(" ", args.Select(QuoteForCommandLine));

        if (File.Exists(shadowDllPath))
        {
            string dotnetArguments = string.IsNullOrWhiteSpace(renderedArgs)
                ? QuoteForCommandLine(shadowDllPath)
                : $"{QuoteForCommandLine(shadowDllPath)} {renderedArgs}";

            return new ProcessStartInfo("dotnet", dotnetArguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };
        }

        string? processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return null;
        }

        string shadowExecutablePath = Path.Combine(runDirectory, Path.GetFileName(processPath));
        if (!File.Exists(shadowExecutablePath))
        {
            return null;
        }

        return new ProcessStartInfo(shadowExecutablePath, renderedArgs)
        {
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };
    }

    private static string GetShadowRootPath()
    {
        string? configuredRoot = Environment.GetEnvironmentVariable(ShadowRootFlag);
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            return Path.GetFullPath(configuredRoot);
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            return Path.Combine(localAppData, "ArchHarness", "shadow-runtime");
        }

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            return Path.Combine(userProfile, ".archharness", "shadow-runtime");
        }

        return Path.Combine(Path.GetTempPath(), "archharness-shadow");
    }

    private static void CopyDirectoryRecursively(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (string sourceFile in Directory.GetFiles(sourceDirectory))
        {
            string destinationFile = Path.Combine(destinationDirectory, Path.GetFileName(sourceFile));
            File.Copy(sourceFile, destinationFile, overwrite: true);
        }

        foreach (string sourceSubDirectory in Directory.GetDirectories(sourceDirectory))
        {
            string destinationSubDirectory = Path.Combine(destinationDirectory, Path.GetFileName(sourceSubDirectory));
            CopyDirectoryRecursively(sourceSubDirectory, destinationSubDirectory);
        }
    }

    private static void CleanupOldShadowRuns(string shadowRoot)
    {
        if (!Directory.Exists(shadowRoot))
        {
            return;
        }

        DateTimeOffset staleThreshold = DateTimeOffset.UtcNow.AddDays(-2);
        foreach (string directory in Directory.GetDirectories(shadowRoot, "run-*", SearchOption.TopDirectoryOnly))
        {
            try
            {
                DateTimeOffset lastWriteUtc = new DateTimeOffset(Directory.GetLastWriteTimeUtc(directory));
                if (lastWriteUtc >= staleThreshold)
                {
                    continue;
                }

                Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // Ignore cleanup failures.
            }
        }
    }

    private static string QuoteForCommandLine(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        bool needsQuotes = value.Any(char.IsWhiteSpace) || value.Contains('"', StringComparison.Ordinal);
        if (!needsQuotes)
        {
            return value;
        }

        return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static bool IsLikelyInteractiveConsole()
    {
        try
        {
            return !Console.IsInputRedirected && !Console.IsOutputRedirected;
        }
        catch
        {
            return false;
        }
    }
}
