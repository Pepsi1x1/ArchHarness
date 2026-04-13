namespace ArchHarness.App.Core;

internal static class WikiDocPathHelper
{
    internal static string EnsureHeading(string markdown, string title)
    {
        string trimmed = markdown.Trim();
        return trimmed.StartsWith('#')
            ? trimmed
            : $"# {title}{Environment.NewLine}{Environment.NewLine}{trimmed}";
    }

    internal static string ToWorkspaceRelativePath(string scanRoot, string fullPath)
        => Path.GetRelativePath(scanRoot, fullPath);
}
