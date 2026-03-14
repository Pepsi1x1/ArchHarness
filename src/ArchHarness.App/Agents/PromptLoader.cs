namespace ArchHarness.App.Agents;

/// <summary>
/// Loads prompt template files from well-known search paths and performs token replacement.
/// </summary>
internal static class PromptLoader
{
    private static readonly string[] SEARCH_ROOTS = new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() };

    /// <summary>
    /// Loads a prompt file from the specified subfolder under the Prompts directory.
    /// </summary>
    /// <param name="subfolder">The subfolder within Prompts.</param>
    /// <param name="fileName">The prompt file name.</param>
    /// <param name="fallbackText">Fallback prompt text used when the file is not found.</param>
    /// <returns>The prompt file content, or the fallback text if not found.</returns>
    public static string Load(string subfolder, string fileName, string fallbackText)
    {
        foreach (string root in SEARCH_ROOTS)
        {
            string path = Path.Combine(root, "Prompts", subfolder, fileName);
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }

        return fallbackText;
    }

    /// <summary>
    /// Replaces placeholder tokens in a prompt template.
    /// </summary>
    /// <param name="template">The template text.</param>
    /// <param name="replacements">Placeholder and replacement value pairs.</param>
    /// <returns>The rendered prompt.</returns>
    public static string Render(string template, params (string Placeholder, string Value)[] replacements)
    {
        string output = template;
        foreach ((string placeholder, string value) in replacements)
        {
            output = output.Replace(placeholder, value ?? string.Empty, StringComparison.Ordinal);
        }

        return output;
    }
}