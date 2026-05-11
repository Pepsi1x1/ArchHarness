using ArchHarness.App.SourceControl;

namespace ArchHarness.App.Core;

/// <summary>
/// Executes arbitrary shell commands inside a workspace using the current platform shell.
/// </summary>
public interface IShellCommandExecutor
{
    /// <summary>
    /// Runs a shell command in the provided working directory and captures its output.
    /// </summary>
    Task<LocalCommandResult> RunAsync(string command, string workingDirectory, CancellationToken cancellationToken);
}

/// <summary>
/// Default implementation of <see cref="IShellCommandExecutor"/>.
/// </summary>
public sealed class ShellCommandExecutor : IShellCommandExecutor
{
    private readonly ILocalCommandRunner _commandRunner;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShellCommandExecutor"/> class.
    /// </summary>
    public ShellCommandExecutor(ILocalCommandRunner commandRunner)
    {
        this._commandRunner = commandRunner;
    }

    /// <inheritdoc />
    public Task<LocalCommandResult> RunAsync(string command, string workingDirectory, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ArgumentException("Command must be non-empty.", nameof(command));
        }

        if (OperatingSystem.IsWindows())
        {
            return this.RunWindowsAsync(command, workingDirectory, cancellationToken);
        }

        return this._commandRunner.RunAsync(
            "/bin/sh",
            new[] { "-lc", command },
            workingDirectory: workingDirectory,
            cancellationToken: cancellationToken);
    }

    private Task<LocalCommandResult> RunWindowsAsync(string command, string workingDirectory, CancellationToken cancellationToken)
    {
        if (this._commandRunner.IsCommandAvailable("pwsh"))
        {
            return this._commandRunner.RunAsync(
                "pwsh",
                new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", command },
                workingDirectory: workingDirectory,
                cancellationToken: cancellationToken);
        }

        if (this._commandRunner.IsCommandAvailable("powershell"))
        {
            return this._commandRunner.RunAsync(
                "powershell",
                new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", command },
                workingDirectory: workingDirectory,
                cancellationToken: cancellationToken);
        }

        return this._commandRunner.RunAsync(
            "cmd",
            new[] { "/d", "/s", "/c", command },
            workingDirectory: workingDirectory,
            cancellationToken: cancellationToken);
    }
}
