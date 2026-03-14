using System.Diagnostics;

namespace ArchHarness.App.Core;

/// <summary>
/// Represents the outcome of a build execution attempt.
/// </summary>
/// <param name="Executed">Whether the build was actually executed.</param>
/// <param name="Passed">Whether the build completed successfully.</param>
/// <param name="ExitCode">The process exit code, or null if not executed.</param>
/// <param name="Output">The combined stdout/stderr output or reason for non-execution.</param>
public sealed record BuildResult(bool Executed, bool Passed, int? ExitCode, string Output)
{
    /// <summary>
    /// Creates a <see cref="BuildResult"/> indicating the build was not executed.
    /// </summary>
    /// <param name="reason">The reason the build was not executed.</param>
    /// <returns>A non-executed build result.</returns>
    public static BuildResult NotExecuted(string reason) => new BuildResult(Executed: false, Passed: false, ExitCode: null, Output: reason);
}

/// <summary>
/// Runs build commands in a subprocess and captures the result.
/// </summary>
public interface IBuildRunner
{
    /// <summary>
    /// Runs the specified build command in the given working directory.
    /// </summary>
    /// <param name="buildCommand">The build command to run, or null to skip.</param>
    /// <param name="workingDirectory">The directory from which to run the build.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The build result with execution status, exit code, and output.</returns>
    Task<BuildResult> RunAsync(string? buildCommand, string workingDirectory, CancellationToken cancellationToken);
}

/// <summary>
/// Default implementation of <see cref="IBuildRunner"/> that invokes dotnet build commands.
/// </summary>
public sealed class BuildRunner : IBuildRunner
{
    /// <inheritdoc />
    public async Task<BuildResult> RunAsync(string? buildCommand, string workingDirectory, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buildCommand))
        {
            return BuildResult.NotExecuted("Build command was not configured.");
        }

        string trimmed = buildCommand.Trim();
        if (!trimmed.StartsWith("dotnet build", StringComparison.OrdinalIgnoreCase))
        {
            return BuildResult.NotExecuted("Build command is not allow-listed. Only 'dotnet build ...' is supported.");
        }

        string args = trimmed.Length == "dotnet".Length ? string.Empty : trimmed["dotnet".Length..].TrimStart();
        ProcessStartInfo info = new ProcessStartInfo("dotnet", args)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using Process process = new Process { StartInfo = info };
        process.Start();
        string stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        string stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        string output = string.Join(Environment.NewLine, new[] { stdout, stderr }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return new BuildResult(Executed: true, Passed: process.ExitCode == 0, ExitCode: process.ExitCode, Output: Redaction.RedactSecrets(output));
    }
}
