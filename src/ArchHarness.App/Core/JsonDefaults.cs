using System.Text.Json;

namespace ArchHarness.App.Core;

/// <summary>
/// Provides shared <see cref="JsonSerializerOptions"/> instances for consistent JSON serialization.
/// </summary>
public static class JsonDefaults
{
    /// <summary>
    /// JSON serializer options configured with indented formatting for human-readable output.
    /// </summary>
    public static readonly JsonSerializerOptions INDENTED = new JsonSerializerOptions() { WriteIndented = true };

    /// <summary>
    /// JSON serializer options with web defaults (camelCase, case-insensitive) and indented formatting.
    /// Used by persistence catalogs that read/write user-scoped JSON files.
    /// </summary>
    public static readonly JsonSerializerOptions WEB_INDENTED = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
}
