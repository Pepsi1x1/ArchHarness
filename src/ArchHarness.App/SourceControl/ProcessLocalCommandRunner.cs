using System.ComponentModel;
using System.Diagnostics;

namespace ArchHarness.App.SourceControl;

/// <summary>
/// Runs local commands using the current process environment.
/// </summary>
public sealed class ProcessLocalCommandRunner : ILocalCommandRunner
{
    /// <inheritdoc />
    public bool IsCommandAvailable(string commandName)
        => TryResolveExecutable(commandName) is not null;

    /// <inheritdoc />
    public async Task<LocalCommandResult> RunAsync(string commandName, IReadOnlyList<string> arguments, string? standardInput = null)
    {
        string executablePath = TryResolveExecutable(commandName)
            ?? throw new Win32Exception($"Command '{commandName}' was not found on PATH.");

        bool redirectStandardInput = standardInput is not null;

        ProcessStartInfo info = new ProcessStartInfo(executablePath)
        {
            RedirectStandardInput = redirectStandardInput,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using Process process = new Process { StartInfo = info };
        process.Start();

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();

        if (redirectStandardInput)
        {
            await process.StandardInput.WriteAsync(standardInput).ConfigureAwait(false);
            process.StandardInput.Close();
        }

        Task waitForExitTask = process.WaitForExitAsync();
        await Task.WhenAll(stdoutTask, stderrTask, waitForExitTask).ConfigureAwait(false);

        return new LocalCommandResult(process.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
    }

    private static string? TryResolveExecutable(string commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName))
        {
            return null;
        }

        if (commandName.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) >= 0)
        {
            return File.Exists(commandName) ? commandName : null;
        }

        string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        string[] directories = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        IEnumerable<string> candidateNames = GetCandidateNames(commandName);

        foreach (string directory in directories)
        {
            foreach (string candidateName in candidateNames)
            {
                string candidatePath = Path.Combine(directory, candidateName);
                if (File.Exists(candidatePath))
                {
                    return candidatePath;
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> GetCandidateNames(string commandName)
    {
        if (!OperatingSystem.IsWindows() || Path.HasExtension(commandName))
        {
            yield return commandName;
            yield break;
        }

        yield return commandName;

        string pathExtensions = Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.COM";
        foreach (string extension in pathExtensions.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return commandName + extension;
        }
    }
}