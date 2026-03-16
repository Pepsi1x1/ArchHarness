namespace ArchHarness.App.Agents;

/// <summary>
/// Shared file discovery logic for loading prompt and guideline files from well-known search paths.
/// </summary>
internal static class FileSearchHelper
{
    private static readonly string[] SEARCH_ROOTS = new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() };

    /// <summary>
    /// Searches well-known root directories for a file under the given category and subfolder.
    /// </summary>
    /// <param name="category">Top-level directory name (e.g. "Prompts", "Guidelines").</param>
    /// <param name="subfolder">Subfolder within the category.</param>
    /// <param name="fileName">The file name to locate.</param>
    /// <param name="fallbackText">Value returned when the file is not found in any search root.</param>
    /// <returns>The file content, or <paramref name="fallbackText"/> if not found.</returns>
    public static string LoadFromSearchRoots(string category, string subfolder, string fileName, string fallbackText)
    {
        foreach (string root in SEARCH_ROOTS)
        {
            string path = Path.Combine(root, category, subfolder, fileName);
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }

        return fallbackText;
    }
}
