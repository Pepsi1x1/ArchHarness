namespace ArchHarness.App.Agents;

/// <summary>
/// Loads guideline markdown files from well-known search paths for agent consumption.
/// </summary>
internal static class GuidelineLoader
{
    private static readonly string[] SearchRoots = new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() };

    /// <summary>
    /// Loads a guideline file from the specified subfolder under the Guidelines directory.
    /// </summary>
    /// <param name="subfolder">The subfolder within Guidelines (e.g. "Style", "Architecture Review", "Builder", "Frontend").</param>
    /// <param name="fileName">The guideline file name.</param>
    /// <param name="fallbackMessage">Message returned when the file is not found.</param>
    /// <returns>The guideline file content, or the fallback message if not found.</returns>
    public static string Load(string subfolder, string fileName, string fallbackMessage)
    {
        foreach (string root in SearchRoots)
        {
            string path = Path.Combine(root, "Guidelines", subfolder, fileName);
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }

        return fallbackMessage;
    }
}
