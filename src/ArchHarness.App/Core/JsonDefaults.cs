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
    public static readonly JsonSerializerOptions Indented = new JsonSerializerOptions() { WriteIndented = true };
}
