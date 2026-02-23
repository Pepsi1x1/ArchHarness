using System.Text.Json;

namespace ArchHarness.App.Core;

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
}
