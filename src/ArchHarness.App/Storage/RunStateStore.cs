using System.Text.Json;
using System.Security;
using ArchHarness.App.Core;

namespace ArchHarness.App.Storage;

/// <summary>
/// Provides access to persisted resumable run checkpoints.
/// </summary>
public interface IRunStateStore
{
    /// <summary>
    /// Writes the current run state checkpoint to disk.
    /// </summary>
    Task WriteStateAsync(string runDirectory, PersistedRunState state, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the current run state checkpoint from disk, if present.
    /// </summary>
    PersistedRunState? GetState(string runDirectory);
}

/// <summary>
/// File-system-backed implementation of <see cref="IRunStateStore"/>.
/// </summary>
public sealed class RunStateStore : IRunStateStore
{
    private const string RUN_STATE_FILE_NAME = "run-state.json";

    /// <inheritdoc />
    public Task WriteStateAsync(string runDirectory, PersistedRunState state, CancellationToken cancellationToken)
    {
        string filePath = FileSystemStorageHelper.GetRunFilePath(runDirectory, RUN_STATE_FILE_NAME);
        return FileSystemStorageHelper.WriteJsonFileAsync(filePath, state, JsonDefaults.WEB_INDENTED, cancellationToken);
    }

    /// <inheritdoc />
    public PersistedRunState? GetState(string runDirectory)
    {
        string filePath = FileSystemStorageHelper.GetRunFilePath(runDirectory, RUN_STATE_FILE_NAME);
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            string json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<PersistedRunState>(json, JsonDefaults.WEB_INDENTED);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (SecurityException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
