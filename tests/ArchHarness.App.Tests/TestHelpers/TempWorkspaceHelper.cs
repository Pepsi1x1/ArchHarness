namespace ArchHarness.App.Tests.TestHelpers;

/// <summary>
/// Shared helpers for creating and cleaning up temporary workspace directories in tests.
/// </summary>
internal static class TempWorkspaceHelper
{
    /// <summary>
    /// Creates a temporary workspace directory under the system temp path.
    /// </summary>
    /// <returns>The full path to the created directory.</returns>
    public static string CreateTempWorkspace()
    {
        string path = Path.Combine(Path.GetTempPath(), "ArchHarness.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Recursively deletes the temporary workspace directory if it exists.
    /// </summary>
    /// <param name="path">The path to clean up.</param>
    public static void CleanupTempWorkspace(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
