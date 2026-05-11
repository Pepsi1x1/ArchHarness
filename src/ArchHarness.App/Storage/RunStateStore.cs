using System.Collections.Concurrent;
using System.Security;
using System.Text.Json;
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
    /// Updates the current run state atomically using the latest persisted value.
    /// Return <see langword="null"/> from <paramref name="updateState"/> to skip the write.
    /// </summary>
    async Task<bool> UpdateStateAsync(string runDirectory, Func<PersistedRunState?, PersistedRunState?> updateState, CancellationToken cancellationToken)
    {
        PersistedRunState? updatedState = updateState(this.GetState(runDirectory));
        if (updatedState is null)
        {
            return false;
        }

        await this.WriteStateAsync(runDirectory, updatedState, cancellationToken).ConfigureAwait(false);
        return true;
    }

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
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> WriteGates = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task WriteStateAsync(string runDirectory, PersistedRunState state, CancellationToken cancellationToken)
    {
        string filePath = FileSystemStorageHelper.GetRunFilePath(runDirectory, RUN_STATE_FILE_NAME);
        SemaphoreSlim gate = GetWriteGate(filePath);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await FileSystemStorageHelper.WriteJsonFileAsync(filePath, state, JsonDefaults.WEB_INDENTED, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateStateAsync(string runDirectory, Func<PersistedRunState?, PersistedRunState?> updateState, CancellationToken cancellationToken)
    {
        string filePath = FileSystemStorageHelper.GetRunFilePath(runDirectory, RUN_STATE_FILE_NAME);
        SemaphoreSlim gate = GetWriteGate(filePath);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            PersistedRunState? currentState = ReadState(filePath);
            PersistedRunState? updatedState = updateState(currentState);
            if (updatedState is null)
            {
                return false;
            }

            await FileSystemStorageHelper.WriteJsonFileAsync(filePath, updatedState, JsonDefaults.WEB_INDENTED, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public PersistedRunState? GetState(string runDirectory)
    {
        string filePath = FileSystemStorageHelper.GetRunFilePath(runDirectory, RUN_STATE_FILE_NAME);
        return ReadState(filePath);
    }

    private static PersistedRunState? ReadState(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            using FileStream stream = FileSystemStorageHelper.OpenReadStreamShared(filePath);
            using StreamReader reader = new(stream);
            string json = reader.ReadToEnd();
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

    private static SemaphoreSlim GetWriteGate(string filePath)
        => WriteGates.GetOrAdd(filePath, static _ => new SemaphoreSlim(1, 1));
}
