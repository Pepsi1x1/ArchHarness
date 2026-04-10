using ArchHarness.App.Workspace;

namespace ArchHarness.App.Tests.Core;

public sealed class FileSystemWorkspaceAdapterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ArchHarnessWorkspaceAdapterTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task InitializeAsync_ExcludesTransientHarnessTempFilesFromBaseline()
    {
        string runDirectory = Path.Combine(this._root, ".agent-harness", "runs", "20260407T105032096");
        Directory.CreateDirectory(runDirectory);
        string tempPath = Path.Combine(runDirectory, ".run-state.json.aaf3db9e9a724bd4b645a195dab250b4.tmp");
        await File.WriteAllTextAsync(tempPath, "temp");

        FileSystemWorkspaceAdapter adapter = new FileSystemWorkspaceAdapter(this._root);

        await adapter.InitializeAsync(projectName: null, initGit: false, CancellationToken.None);

        File.Delete(tempPath);

        string diff = await adapter.DiffAsync(CancellationToken.None);

        Assert.DoesNotContain(".run-state.json.aaf3db9e9a724bd4b645a195dab250b4.tmp", diff, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DiffAsync_IgnoresTransientHarnessTempFilesCreatedAfterBaseline()
    {
        FileSystemWorkspaceAdapter adapter = new FileSystemWorkspaceAdapter(this._root);
        await adapter.InitializeAsync(projectName: null, initGit: false, CancellationToken.None);

        string runDirectory = Path.Combine(this._root, ".agent-harness", "runs", "20260407T105032096");
        Directory.CreateDirectory(runDirectory);
        string tempPath = Path.Combine(runDirectory, ".run-state.json.b88d973ff55f45128446b7a07728f27e.tmp");
        await File.WriteAllTextAsync(tempPath, "temp");

        string diff = await adapter.DiffAsync(CancellationToken.None);

        Assert.DoesNotContain(".run-state.json.b88d973ff55f45128446b7a07728f27e.tmp", diff, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(this._root))
        {
            Directory.Delete(this._root, recursive: true);
        }
    }
}
