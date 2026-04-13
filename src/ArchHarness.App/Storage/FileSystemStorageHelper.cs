using System.Text.Json;

namespace ArchHarness.App.Storage;

/// <summary>
/// Provides shared file I/O helpers for file-system-backed catalog classes.
/// Centralises the repeated pattern of resolving the application data directory,
/// ensuring the directory exists before writing, serialising to JSON, and writing atomically.
/// </summary>
internal static class FileSystemStorageHelper
{
    private static readonly TimeSpan[] AtomicReplaceRetryDelays =
    [
        TimeSpan.FromMilliseconds(20),
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(100)
    ];

    /// <summary>
    /// Returns the default per-user file path for the given storage file name
    /// under <c>%APPDATA%\ArchHarness\</c> (or the platform equivalent).
    /// </summary>
    /// <param name="fileName">The file name (e.g. <c>settings.json</c>).</param>
    /// <returns>The fully-qualified default storage file path.</returns>
    internal static string GetAppDataFilePath(string fileName)
    {
        string appDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return NormalizePath(Path.Combine(appDataRoot, "ArchHarness", fileName));
    }

    /// <summary>
    /// Returns a canonicalized full path with environment variables expanded.
    /// </summary>
    internal static string NormalizePath(string path)
        => Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));

    /// <summary>
    /// Returns the canonical runs root for a workspace.
    /// </summary>
    internal static string GetRunsRootPath(string workspaceRoot)
        => NormalizePath(Path.Combine(workspaceRoot, ".agent-harness", "runs"));

    /// <summary>
    /// Returns the canonical path to a file inside a run directory.
    /// </summary>
    internal static string GetRunFilePath(string runDirectory, string fileName)
        => NormalizePath(Path.Combine(runDirectory, fileName));

    /// <summary>
    /// Serialises <paramref name="value"/> to JSON and writes it to <paramref name="filePath"/>,
    /// creating the parent directory if it does not already exist.
    /// </summary>
    /// <typeparam name="T">The type to serialise.</typeparam>
    /// <param name="filePath">The fully-qualified destination file path.</param>
    /// <param name="value">The value to serialise.</param>
    /// <param name="options">JSON serialisation options.</param>
    internal static void WriteJsonFile<T>(string filePath, T value, JsonSerializerOptions options)
    {
        string normalizedPath = NormalizePath(filePath);
        string directory = EnsureParentDirectory(normalizedPath);
        string json = JsonSerializer.Serialize(value, options);
        string tempPath = CreateSiblingTempFilePath(directory, normalizedPath);

        try
        {
            File.WriteAllText(tempPath, json);
            ReplaceFileAtomically(tempPath, normalizedPath);
        }
        finally
        {
            DeleteTempFileIfPresent(tempPath);
        }
    }

    /// <summary>
    /// Serialises <paramref name="value"/> to JSON and writes it asynchronously.
    /// </summary>
    internal static Task WriteJsonFileAsync<T>(string filePath, T value, JsonSerializerOptions options, CancellationToken cancellationToken)
        => WriteJsonFileInternalAsync(filePath, value, options, cancellationToken);

    /// <summary>
    /// Opens a file for shared reads without blocking atomic replacement by writers.
    /// </summary>
    internal static FileStream OpenReadStreamShared(string filePath)
    {
        string normalizedPath = NormalizePath(filePath);
        return new FileStream(
            normalizedPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            4096,
            FileOptions.SequentialScan);
    }

    private static async Task WriteJsonFileInternalAsync<T>(string filePath, T value, JsonSerializerOptions options, CancellationToken cancellationToken)
    {
        string normalizedPath = NormalizePath(filePath);
        string directory = EnsureParentDirectory(normalizedPath);
        string json = JsonSerializer.Serialize(value, options);
        string tempPath = CreateSiblingTempFilePath(directory, normalizedPath);

        try
        {
            await File.WriteAllTextAsync(tempPath, json, cancellationToken);
            await ReplaceFileAtomicallyAsync(tempPath, normalizedPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            DeleteTempFileIfPresent(tempPath);
        }
    }

    private static string EnsureParentDirectory(string normalizedPath)
    {
        string? directory = Path.GetDirectoryName(normalizedPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException($"A parent directory is required for '{normalizedPath}'.");
        }

        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string CreateSiblingTempFilePath(string directory, string destinationPath)
        => Path.Combine(directory, $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");

    private static void ReplaceFileAtomically(string tempPath, string destinationPath)
    {
        int attempt = 0;
        while (true)
        {
            try
            {
                ReplaceFileAtomicallyCore(tempPath, destinationPath);
                return;
            }
            catch (Exception ex) when (ShouldRetryAtomicReplace(ex, attempt))
            {
                Thread.Sleep(AtomicReplaceRetryDelays[attempt]);
                attempt++;
            }
        }
    }

    private static async Task ReplaceFileAtomicallyAsync(string tempPath, string destinationPath, CancellationToken cancellationToken)
    {
        int attempt = 0;
        while (true)
        {
            try
            {
                ReplaceFileAtomicallyCore(tempPath, destinationPath);
                return;
            }
            catch (Exception ex) when (ShouldRetryAtomicReplace(ex, attempt))
            {
                await Task.Delay(AtomicReplaceRetryDelays[attempt], cancellationToken).ConfigureAwait(false);
                attempt++;
            }
        }
    }

    private static void ReplaceFileAtomicallyCore(string tempPath, string destinationPath)
    {
        if (!File.Exists(destinationPath))
        {
            File.Move(tempPath, destinationPath);
            return;
        }

        try
        {
            File.Replace(tempPath, destinationPath, destinationBackupFileName: null);
        }
        catch (PlatformNotSupportedException)
        {
            File.Move(tempPath, destinationPath, overwrite: true);
        }
    }

    private static bool ShouldRetryAtomicReplace(Exception exception, int attempt)
        => attempt < AtomicReplaceRetryDelays.Length
            && (exception is IOException || exception is UnauthorizedAccessException);

    private static void DeleteTempFileIfPresent(string tempPath)
    {
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }
    }
}
