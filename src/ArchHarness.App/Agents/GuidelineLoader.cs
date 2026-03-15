namespace ArchHarness.App.Agents;

/// <summary>
/// Loads guideline markdown files from well-known search paths for agent consumption.
/// </summary>
internal static class GuidelineLoader
{
    /// <summary>
    /// Loads a guideline file from the specified subfolder under the Guidelines directory.
    /// </summary>
    /// <param name="subfolder">The subfolder within Guidelines (e.g. "CodingStyle", "Security", "Architecture Review", "Backend Developer", "Frontend Developer").</param>
    /// <param name="fileName">The guideline file name.</param>
    /// <param name="fallbackMessage">Message returned when the file is not found.</param>
    /// <returns>The guideline file content, or the fallback message if not found.</returns>
    public static string Load(string subfolder, string fileName, string fallbackMessage)
        => FileSearchHelper.LoadFromSearchRoots("Guidelines", subfolder, fileName, fallbackMessage);
}
