namespace ArchHarness.App.Core;

/// <summary>
/// Shared helpers for architecture-loop mode workspace file enumeration and prompt building.
/// </summary>
internal static class ArchitectureLoopHelpers
{
    /// <summary>
    /// Enumerates source files in the workspace, filtering by language extensions and excluding build output directories.
    /// </summary>
    /// <param name="workspaceRoot">The root directory of the workspace.</param>
    /// <param name="languages">Optional language scope list (e.g. "dotnet", "vue3").</param>
    /// <returns>Relative paths of matching workspace files.</returns>
    public static IReadOnlyList<string> EnumerateWorkspaceFiles(string workspaceRoot, IReadOnlyList<string>? languages)
    {
        HashSet<string> extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".cs" };
        if (languages?.Any(l => string.Equals(l, "vue3", StringComparison.OrdinalIgnoreCase)) == true)
        {
            extensions.UnionWith(new[] { ".vue", ".ts", ".tsx", ".js", ".jsx" });
        }

        return Directory
            .EnumerateFiles(workspaceRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => !path.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase) &&
                           !path.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase))
            .Where(path => extensions.Contains(Path.GetExtension(path)))
            .Select(path => Path.GetRelativePath(workspaceRoot, path))
            .ToArray();
    }

    /// <summary>
    /// Builds a delegated prompt decorated with architecture-loop session metadata.
    /// </summary>
    /// <param name="objective">The base objective or remediation prompt.</param>
    /// <param name="workspaceRoot">The root directory of the workspace.</param>
    /// <param name="architectureLoopPrompt">Optional user-supplied architecture loop prompt to append.</param>
    /// <returns>The decorated prompt string.</returns>
    public static string BuildArchitectureLoopPrompt(string objective, string workspaceRoot, string? architectureLoopPrompt)
    {
        string promptSection = string.IsNullOrWhiteSpace(architectureLoopPrompt)
            ? string.Empty
            : $"{Environment.NewLine}ArchitectureLoopPrompt: {architectureLoopPrompt.Trim()}";
        return $"""
            SessionMode: architecture-loop
            WorkspaceScope: entire workspace at {workspaceRoot}
            {objective}{promptSection}
            """;
    }
}
