using ArchHarness.App.Core;
using ArchHarness.App.SourceControl;

namespace ArchHarness.App.Tests.Core;

public sealed class VerificationCommandRunnerTests
{
    [Fact]
    public async Task RunAsync_CapturesPassingAndFailingEvidence()
    {
        string workspaceRoot = Path.Combine(Path.GetTempPath(), $"archharness-verification-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspaceRoot);

        try
        {
            VerificationCommandRunner runner = new VerificationCommandRunner(new ShellCommandExecutor(new ProcessLocalCommandRunner()));
            IReadOnlyList<VerificationEvidence> evidence = await runner.RunAsync(
                workspaceRoot,
                new[]
                {
                    new VerificationCommand("Dotnet version", "dotnet --version", "runtime", "Runtime available", true),
                    new VerificationCommand("Invalid dotnet command", "dotnet __archharness_invalid_subcommand__", "test", "Invalid command should fail", true)
                },
                progress: null,
                CancellationToken.None);

            Assert.Equal(2, evidence.Count);
            Assert.True(evidence[0].Passed);
            Assert.False(evidence[1].Passed);
            Assert.Equal("Runtime available", evidence[0].Criterion);
            Assert.Equal("Invalid command should fail", evidence[1].Criterion);
            Assert.Contains("Dotnet version", evidence[0].Summary, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }
}
