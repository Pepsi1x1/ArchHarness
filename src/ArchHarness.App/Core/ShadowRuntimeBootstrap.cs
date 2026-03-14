using System.Diagnostics;
using System.Reflection;

namespace ArchHarness.App.Core;

/// <summary>
/// Provides shadow-copy relaunch capability so the main application can be updated while running.
/// </summary>
public static class ShadowRuntimeBootstrap
{
    private const string SHADOW_RUN_FLAG = "ARCHHARNESS_SHADOW_RUN";
    private const string SHADOW_DISABLE_FLAG = "ARCHHARNESS_SHADOW_DISABLE";
    private const string SHADOW_FORCE_FLAG = "ARCHHARNESS_SHADOW_FORCE";
    private const string ORIGINAL_BASE_DIR_FLAG = "ARCHHARNESS_ORIGINAL_BASEDIR";
    private const string SHADOW_ROOT_FLAG = "ARCHHARNESS_SHADOW_ROOT";

    /// <summary>
    /// Attempts to relaunch the application from a shadow copy directory, returning true if relaunch was initiated.
    /// </summary>
    /// <param name="args">The original command-line arguments to forward.</param>
    /// <returns>True if the process was relaunched from a shadow copy; false to continue in-place.</returns>
    public static bool TryRelaunchFromShadowCopy(string[] args)
    {
        if (string.Equals(Environment.GetEnvironmentVariable(SHADOW_DISABLE_FLAG), "1", StringComparison.Ordinal))
        {
            return false;
        }

        if (string.Equals(Environment.GetEnvironmentVariable(SHADOW_RUN_FLAG), "1", StringComparison.Ordinal))
        {
            return false;
        }

        // Keep interactive setup sessions in-process by default.
        // Explicit CLI run invocations can still use shadow mode safely.
        bool forceShadow = string.Equals(Environment.GetEnvironmentVariable(SHADOW_FORCE_FLAG), "1", StringComparison.Ordinal);
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
            relaunchStartInfo.EnvironmentVariables[SHADOW_RUN_FLAG] = "1";
            relaunchStartInfo.EnvironmentVariables[ORIGINAL_BASE_DIR_FLAG] = sourceBaseDirectory;

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

        if (File.Exists(shadowDllPath))
        {
            ProcessStartInfo info = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };
            info.ArgumentList.Add(shadowDllPath);
            foreach (string arg in args)
            {
                info.ArgumentList.Add(arg);
            }

            return info;
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

        ProcessStartInfo execInfo = new ProcessStartInfo(shadowExecutablePath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };
        foreach (string arg in args)
        {
            execInfo.ArgumentList.Add(arg);
        }

        return execInfo;
    }

    private static string GetShadowRootPath()
    {
        string? configuredRoot = Environment.GetEnvironmentVariable(SHADOW_ROOT_FLAG);
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
