using System.Diagnostics;
using ArchHarness.App.Workspace;

namespace ArchHarness.App.Tests.Core;

public sealed class GitWorkspaceAdapterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ArchHarnessGitWorkspaceAdapterTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DiffAsync_ExcludesAgentHarnessDirectoryChanges()
    {
        Directory.CreateDirectory(this._root);
        this.RunGit("init");
        this.RunGit("config user.email archharness-tests@example.com");
        this.RunGit("config user.name ArchHarnessTests");

        string trackedFile = Path.Combine(this._root, "tracked.txt");
        await File.WriteAllTextAsync(trackedFile, "baseline");
        this.RunGit("add tracked.txt");
        this.RunGit("commit -m initial");

        GitWorkspaceAdapter adapter = new GitWorkspaceAdapter(this._root);
        await adapter.InitializeAsync(projectName: null, initGit: false, CancellationToken.None);

        string harnessDirectory = Path.Combine(this._root, ".agent-harness", "runs", "run-1");
        Directory.CreateDirectory(harnessDirectory);
        await File.WriteAllTextAsync(Path.Combine(harnessDirectory, "state.json"), "ignored");
        await File.WriteAllTextAsync(trackedFile, "updated");

        string diff = await adapter.DiffAsync(CancellationToken.None);

        Assert.Contains("tracked.txt", diff, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".agent-harness", diff, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(this._root))
        {
            foreach (string path in Directory.GetFileSystemEntries(this._root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(path, FileAttributes.Normal);
            }

            File.SetAttributes(this._root, FileAttributes.Normal);
            Directory.Delete(this._root, recursive: true);
        }
    }

    private void RunGit(string arguments)
    {
        ProcessStartInfo info = new ProcessStartInfo("git")
        {
            WorkingDirectory = this._root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (string part in arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            info.ArgumentList.Add(part);
        }

        using Process process = Process.Start(info) ?? throw new InvalidOperationException($"Failed to start git {arguments}.");
        process.WaitForExit();
        string stderr = process.StandardError.ReadToEnd();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {arguments} failed: {stderr}");
        }
    }
}
