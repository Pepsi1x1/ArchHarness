using System.Diagnostics;

namespace ArchHarness.App.Core;

public sealed record BuildResult(bool Executed, bool Passed, int? ExitCode, string Output)
{
    public static BuildResult NotExecuted(string reason) => new(Executed: false, Passed: false, ExitCode: null, Output: reason);
}

public interface IBuildRunner
{
    Task<BuildResult> RunAsync(string? buildCommand, string workingDirectory, CancellationToken cancellationToken);
}

public sealed class BuildRunner : IBuildRunner
{
    public async Task<BuildResult> RunAsync(string? buildCommand, string workingDirectory, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buildCommand))
        {
            return BuildResult.NotExecuted("Build command was not configured.");
        }

        var trimmed = buildCommand.Trim();
        if (!trimmed.StartsWith("dotnet build", StringComparison.OrdinalIgnoreCase))
        {
            return BuildResult.NotExecuted("Build command is not allow-listed. Only 'dotnet build ...' is supported.");
        }

        var args = trimmed.Length == "dotnet".Length ? string.Empty : trimmed["dotnet".Length..].TrimStart();
        var info = new ProcessStartInfo("dotnet", args)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = info };
        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var output = string.Join(Environment.NewLine, new[] { stdout, stderr }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return new BuildResult(Executed: true, Passed: process.ExitCode == 0, ExitCode: process.ExitCode, Output: Redaction.RedactSecrets(output));
    }
}
