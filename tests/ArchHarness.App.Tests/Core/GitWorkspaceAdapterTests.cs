using ArchHarness.App.Core;
using ArchHarness.App.Workspace;
using LibGit2Sharp;

namespace ArchHarness.App.Tests.Core;

public sealed class GitWorkspaceAdapterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ArchHarnessGitWorkspaceAdapterTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DiffAsync_ExcludesAgentHarnessDirectoryChanges()
    {
        Directory.CreateDirectory(this._root);
        this.InitializeRepository();

        string trackedFile = Path.Combine(this._root, "tracked.txt");
        await File.WriteAllTextAsync(trackedFile, "baseline");
        this.Commit("initial", "tracked.txt");

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

    [Fact]
    public async Task DiffAsync_ExcludesGitIgnoredSnapshotChanges()
    {
        Directory.CreateDirectory(this._root);
        this.InitializeRepositoryWithIgnoreFile();

        string trackedFile = Path.Combine(this._root, "tracked.txt");
        await File.WriteAllTextAsync(trackedFile, "baseline");
        this.Commit("initial", ".gitignore", "tracked.txt");

        GitWorkspaceAdapter adapter = new GitWorkspaceAdapter(this._root);
        await adapter.InitializeAsync(projectName: null, initGit: false, CancellationToken.None);

        string ignoredPackageFile = Path.Combine(this._root, "node_modules", "package", "index.js");
        Directory.CreateDirectory(Path.GetDirectoryName(ignoredPackageFile)!);
        await File.WriteAllTextAsync(ignoredPackageFile, "ignored");
        await File.WriteAllTextAsync(Path.Combine(this._root, "ignored.txt"), "ignored");
        await File.WriteAllTextAsync(trackedFile, "updated");

        string diff = await adapter.DiffAsync(CancellationToken.None);

        Assert.Contains("tracked.txt", diff, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("node_modules", diff, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ignored.txt", diff, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DetectChanges_ExcludesGitIgnoredFiles()
    {
        Directory.CreateDirectory(this._root);
        this.InitializeRepositoryWithIgnoreFile();
        this.Commit("initial", ".gitignore");

        Dictionary<string, (long Length, long LastWriteUtcTicks)> baseline = WorkspaceSnapshotHelper.CaptureSnapshot(this._root);

        string ignoredPackageFile = Path.Combine(this._root, "node_modules", "package", "index.js");
        Directory.CreateDirectory(Path.GetDirectoryName(ignoredPackageFile)!);
        await File.WriteAllTextAsync(ignoredPackageFile, "ignored");
        await File.WriteAllTextAsync(Path.Combine(this._root, "ignored.txt"), "ignored");
        Directory.CreateDirectory(Path.Combine(this._root, "src"));
        await File.WriteAllTextAsync(Path.Combine(this._root, "src", "app.cs"), "visible");

        IReadOnlyList<string> changes = WorkspaceSnapshotHelper.DetectChanges(this._root, baseline);

        Assert.Contains("src/app.cs", changes.Select(path => path.Replace('\\', '/')));
        Assert.DoesNotContain(changes, path => path.Contains("node_modules", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(changes, path => path.EndsWith("ignored.txt", StringComparison.OrdinalIgnoreCase));
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

    private void InitializeRepositoryWithIgnoreFile()
    {
        this.InitializeRepository();
        File.WriteAllText(Path.Combine(this._root, ".gitignore"), $"node_modules/{Environment.NewLine}ignored.txt{Environment.NewLine}");
    }

    private void InitializeRepository()
        => Repository.Init(this._root);

    private void Commit(string message, params string[] relativePaths)
    {
        using Repository repository = new Repository(this._root);
        foreach (string relativePath in relativePaths)
        {
            Commands.Stage(repository, relativePath);
        }

        Signature signature = new Signature("ArchHarness Tests", "archharness-tests@example.com", DateTimeOffset.UtcNow);
        repository.Commit(message, signature, signature);
    }
}
