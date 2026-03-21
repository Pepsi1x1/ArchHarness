using ArchHarness.App.SourceControl;

namespace ArchHarness.App.Tests.Core;

public sealed class ProcessLocalCommandRunnerTests
{
    [Fact]
    public async Task RunAsync_CapturesStdoutAndStderrWithoutBlocking()
    {
        ProcessLocalCommandRunner runner = new ProcessLocalCommandRunner();
        (string command, string[] arguments)? invocation = CreateLargeOutputInvocation(runner);
        if (invocation is null)
        {
            return;
        }

        LocalCommandResult result = await runner.RunAsync(invocation.Value.command, invocation.Value.arguments);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("out-1", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("out-4000", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("err-1", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("err-4000", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ForwardsStandardInputWhenProvided()
    {
        ProcessLocalCommandRunner runner = new ProcessLocalCommandRunner();
        (string command, string[] arguments)? invocation = CreateEchoStdInInvocation(runner);
        if (invocation is null)
        {
            return;
        }

        LocalCommandResult result = await runner.RunAsync(invocation.Value.command, invocation.Value.arguments, standardInput: "hello from stdin");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello from stdin", result.StandardOutput, StringComparison.Ordinal);
    }

    private static (string command, string[] arguments)? CreateLargeOutputInvocation(ProcessLocalCommandRunner runner)
    {
        if (OperatingSystem.IsWindows())
        {
            string? shell = ResolveWindowsPowerShellCommand(runner);
            if (shell is null)
            {
                return null;
            }

            return (shell, new[]
            {
                "-NoProfile",
                "-Command",
                "1..4000 | ForEach-Object { Write-Output \"out-$_\"; [Console]::Error.WriteLine(\"err-$_\") }"
            });
        }

        return ("/bin/sh", new[]
        {
            "-c",
            "i=1; while [ $i -le 4000 ]; do printf 'out-%s\\n' \"$i\"; printf 'err-%s\\n' \"$i\" 1>&2; i=$((i+1)); done"
        });
    }

    private static (string command, string[] arguments)? CreateEchoStdInInvocation(ProcessLocalCommandRunner runner)
    {
        if (OperatingSystem.IsWindows())
        {
            string? shell = ResolveWindowsPowerShellCommand(runner);
            if (shell is null)
            {
                return null;
            }

            return (shell, new[]
            {
                "-NoProfile",
                "-Command",
                "$value = [Console]::In.ReadToEnd(); Write-Output $value"
            });
        }

        return ("/bin/sh", new[]
        {
            "-c",
            "cat"
        });
    }

    private static string? ResolveWindowsPowerShellCommand(ProcessLocalCommandRunner runner)
    {
        if (runner.IsCommandAvailable("pwsh"))
        {
            return "pwsh";
        }

        if (runner.IsCommandAvailable("powershell"))
        {
            return "powershell";
        }

        return null;
    }
}