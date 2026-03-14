namespace ArchHarness.App.Agents;

/// <summary>
/// Shared utility for resolving candidate source files from a diff and workspace root.
/// Eliminates duplicated path-normalization, bin/obj filtering, and fallback logic
/// between <see cref="AnalysisRunner"/> and <see cref="SecurityAnalysisRunner"/>.
/// </summary>
internal static class CandidateFileResolver
{
    private static readonly string[] EXCLUDED_SEGMENTS = ["/bin/", "/obj/", "/node_modules/", "/.git/"];

    /// <summary>
    /// Normalizes the workspace root path to always end with a directory separator.
    /// </summary>
    public static string NormalizeRoot(string workspaceRoot)
    {
        return Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
    }

    /// <summary>
    /// Returns true when the given path is inside an excluded build-output or tool directory.
    /// </summary>
    public static bool IsExcludedDirectory(string path)
    {
        string normalized = path.Replace('\\', '/');
        return EXCLUDED_SEGMENTS.Any(segment => normalized.Contains(segment, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Resolves a single diff line or touched-file entry to an absolute path, validating it
    /// lives inside the workspace root and that the file exists on disk.
    /// </summary>
    /// <param name="entry">A relative or absolute file path.</param>
    /// <param name="workspaceRoot">The workspace root directory.</param>
    /// <param name="normalizedRoot">The normalized root (from <see cref="NormalizeRoot"/>).</param>
    /// <returns>The validated absolute path, or <c>null</c> if the entry is invalid.</returns>
    public static string? TryResolve(string entry, string workspaceRoot, string normalizedRoot)
    {
        string fullPath = Path.IsPathRooted(entry)
            ? Path.GetFullPath(entry)
            : Path.GetFullPath(Path.Combine(workspaceRoot, entry));

        if (fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath))
        {
            return fullPath;
        }

        return null;
    }

    /// <summary>
    /// Splits a diff into non-empty lines suitable for file-path extraction.
    /// </summary>
    public static string[] SplitDiffLines(string diff)
    {
        return diff.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
