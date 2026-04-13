using System.Text.Json;
using ArchHarness.App.Storage;

namespace ArchHarness.App.Tests.Core;

public sealed class FileSystemStorageHelperTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ArchHarnessStorageHelperTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void WriteJsonFile_OverwritesExistingFileWithoutLeavingTempFiles()
    {
        string filePath = Path.Combine(this._root, "settings.json");
        Directory.CreateDirectory(this._root);
        File.WriteAllText(filePath, "stale");

        FileSystemStorageHelper.WriteJsonFile(filePath, new TestPayload("updated"), JsonSerializerOptions.Web);

        Assert.Equal("{\"value\":\"updated\"}", File.ReadAllText(filePath));
        this.AssertNoTempFiles();
    }

    [Fact]
    public async Task WriteJsonFileAsync_OverwritesExistingFileWithoutLeavingTempFilesAsync()
    {
        string filePath = Path.Combine(this._root, "settings.json");
        Directory.CreateDirectory(this._root);
        await File.WriteAllTextAsync(filePath, "stale");

        await FileSystemStorageHelper.WriteJsonFileAsync(filePath, new TestPayload("updated"), JsonSerializerOptions.Web, CancellationToken.None);

        Assert.Equal("{\"value\":\"updated\"}", await File.ReadAllTextAsync(filePath));
        this.AssertNoTempFiles();
    }

    [Fact]
    public async Task WriteJsonFileAsync_RetriesWhileDestinationFileIsTemporarilyLockedAsync()
    {
        string filePath = Path.Combine(this._root, "settings.json");
        Directory.CreateDirectory(this._root);
        await File.WriteAllTextAsync(filePath, "stale");

        FileStream lockStream = new(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        try
        {
            Task writeTask = FileSystemStorageHelper.WriteJsonFileAsync(filePath, new TestPayload("updated"), JsonSerializerOptions.Web, CancellationToken.None);
            await Task.Delay(60);
            await lockStream.DisposeAsync();

            await writeTask;
        }
        finally
        {
            await lockStream.DisposeAsync();
        }

        Assert.Equal("{\"value\":\"updated\"}", await File.ReadAllTextAsync(filePath));
        this.AssertNoTempFiles();
    }

    [Fact]
    public void WriteJsonFile_CreatesParentDirectoryBeforeAtomicReplace()
    {
        string filePath = Path.Combine(this._root, "nested", "settings.json");

        FileSystemStorageHelper.WriteJsonFile(filePath, new TestPayload("created"), JsonSerializerOptions.Web);

        Assert.True(File.Exists(filePath));
        Assert.Equal("{\"value\":\"created\"}", File.ReadAllText(filePath));
        this.AssertNoTempFiles();
    }

    public void Dispose()
    {
        if (Directory.Exists(this._root))
        {
            Directory.Delete(this._root, recursive: true);
        }
    }

    private void AssertNoTempFiles()
    {
        string[] tempFiles = Directory.Exists(this._root)
            ? Directory.GetFiles(this._root, ".*.tmp", SearchOption.AllDirectories)
            : Array.Empty<string>();

        Assert.Empty(tempFiles);
    }

    private sealed record TestPayload(string Value);
}
