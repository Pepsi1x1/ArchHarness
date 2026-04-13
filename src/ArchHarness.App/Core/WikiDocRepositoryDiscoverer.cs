#pragma warning disable S2325 // DI-injectable sealed class; instance method is correct for the abstraction boundary even without current instance state.

namespace ArchHarness.App.Core;

/// <summary>
/// Discovers Git repositories under a scan root directory.
/// </summary>
public sealed class WikiDocRepositoryDiscoverer
{
    private static readonly HashSet<string> IgnoredDirectoryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".agent-harness",
        ".git",
        ".vs",
        "bin",
        "node_modules",
        "obj"
    };

    /// <summary>
    /// Discovers all Git repositories under <paramref name="scanRoot"/> and returns them ordered by relative path.
    /// </summary>
    public IReadOnlyList<WikiDocRepositoryInfo> Discover(string scanRoot)
    {
        HashSet<string> repositories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Stack<string> pending = new Stack<string>();
        pending.Push(scanRoot);

        while (pending.Count > 0)
        {
            string current = pending.Pop();
            string fullCurrent = Path.GetFullPath(current);
            if (HasGitMarker(fullCurrent))
            {
                repositories.Add(fullCurrent);
            }

            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(fullCurrent);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string child in children)
            {
                if (ShouldSkipDirectory(child))
                {
                    continue;
                }

                pending.Push(child);
            }
        }

        return repositories
            .Select(path => CreateRepositoryInfo(scanRoot, path))
            .OrderBy(info => info.RelativePath == "." ? string.Empty : info.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static WikiDocRepositoryInfo CreateRepositoryInfo(string scanRoot, string repositoryRoot)
    {
        string relativePath = Path.GetRelativePath(scanRoot, repositoryRoot);
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            relativePath = ".";
        }

        string normalizedRelativePath = string.Equals(relativePath, ".", StringComparison.Ordinal)
            ? "."
            : relativePath.Replace(Path.DirectorySeparatorChar, '/');
        string directoryName = Path.GetFileName(repositoryRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string displayName = string.Equals(normalizedRelativePath, ".", StringComparison.Ordinal)
            ? directoryName
            : normalizedRelativePath;

        return new WikiDocRepositoryInfo(
            repositoryRoot,
            normalizedRelativePath,
            displayName,
            SanitizePathToken(normalizedRelativePath));
    }

    private static bool HasGitMarker(string directoryPath)
        => Directory.Exists(Path.Combine(directoryPath, ".git"))
            || File.Exists(Path.Combine(directoryPath, ".git"));

    private static bool ShouldSkipDirectory(string directoryPath)
    {
        string name = Path.GetFileName(directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (IgnoredDirectoryNames.Contains(name))
        {
            return true;
        }

        try
        {
            return (File.GetAttributes(directoryPath) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static string SanitizePathToken(string value)
    {
        string normalized = string.Equals(value, ".", StringComparison.Ordinal) ? "root" : value;
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string(normalized
            .Replace('/', '_')
            .Replace('\\', '_')
            .Select(ch => invalid.Contains(ch) ? '_' : ch)
            .ToArray());
    }
}
