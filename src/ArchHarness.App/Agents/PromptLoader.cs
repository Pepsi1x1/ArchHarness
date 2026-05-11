namespace ArchHarness.App.Agents;

/// <summary>
/// Loads prompt template files from well-known search paths and performs token replacement.
/// </summary>
internal static class PromptLoader
{
    /// <summary>
    /// Loads a prompt file from the specified subfolder under the Prompts directory.
    /// </summary>
    /// <param name="subfolder">The subfolder within Prompts.</param>
    /// <param name="fileName">The prompt file name.</param>
    /// <returns>The prompt file content.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the prompt file cannot be loaded.</exception>
    public static string Load(string subfolder, string fileName)
    {
        if (FileSearchHelper.TryLoadFromSearchRoots("Prompts", subfolder, fileName, out string content))
        {
            return content;
        }

        throw new InvalidOperationException($"Required prompt file could not be loaded: Prompts/{subfolder}/{fileName}.");
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
