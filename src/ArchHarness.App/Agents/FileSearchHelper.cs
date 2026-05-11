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
        => TryLoadFromSearchRoots(category, subfolder, fileName, out string content) ? content : fallbackText;

    /// <summary>
    /// Searches well-known root directories for a file under the given category and subfolder.
    /// </summary>
    /// <param name="category">Top-level directory name (e.g. "Prompts", "Guidelines").</param>
    /// <param name="subfolder">Subfolder within the category.</param>
    /// <param name="fileName">The file name to locate.</param>
    /// <param name="content">The file content when found.</param>
    /// <returns><c>true</c> when the file was found and loaded; otherwise <c>false</c>.</returns>
    public static bool TryLoadFromSearchRoots(string category, string subfolder, string fileName, out string content)
    {
        foreach (string root in SEARCH_ROOTS)
        {
            string path = Path.Combine(root, category, subfolder, fileName);
            if (File.Exists(path))
            {
                content = File.ReadAllText(path);
                return true;
            }
        }

        content = string.Empty;
        return false;
    }
}
