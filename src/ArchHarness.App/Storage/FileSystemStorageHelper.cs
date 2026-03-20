using System.Text.Json;

namespace ArchHarness.App.Storage;

/// <summary>
/// Provides shared file I/O helpers for file-system-backed catalog classes.
/// Centralises the repeated pattern of resolving the application data directory,
/// ensuring the directory exists before writing, serialising to JSON, and writing atomically.
/// </summary>
internal static class FileSystemStorageHelper
{
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
        string? directory = Path.GetDirectoryName(normalizedPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(value, options);
        File.WriteAllText(normalizedPath, json);
    }

    /// <summary>
    /// Serialises <paramref name="value"/> to JSON and writes it asynchronously.
    /// </summary>
    internal static Task WriteJsonFileAsync<T>(string filePath, T value, JsonSerializerOptions options, CancellationToken cancellationToken)
    {
        string normalizedPath = NormalizePath(filePath);
        string? directory = Path.GetDirectoryName(normalizedPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(value, options);
        return File.WriteAllTextAsync(normalizedPath, json, cancellationToken);
    }
}
