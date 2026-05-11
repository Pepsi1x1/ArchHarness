using LibGit2Sharp;

namespace ArchHarness.App.Core;

/// <summary>
/// Provides workspace file snapshot and change-detection utilities shared across agents.
/// </summary>
internal static class WorkspaceSnapshotHelper
{
    /// <summary>
    /// Captures a snapshot of all non-ignored files in the workspace, keyed by relative path.
    /// </summary>
    /// <param name="workspaceRoot">The root directory of the workspace.</param>
    /// <returns>A dictionary mapping relative paths to their size and last-write timestamps.</returns>
    public static Dictionary<string, (long Length, long LastWriteUtcTicks)> CaptureSnapshot(string workspaceRoot)
    {
        Dictionary<string, (long Length, long LastWriteUtcTicks)> snapshot = new Dictionary<string, (long Length, long LastWriteUtcTicks)>(StringComparer.OrdinalIgnoreCase);
        string normalizedRoot = NormalizeRoot(workspaceRoot);
        foreach (string relativePath in EnumerateSnapshotFiles(normalizedRoot))
        {
            string fullPath = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
            FileInfo info = new FileInfo(fullPath);
            snapshot[relativePath] = (info.Length, info.LastWriteTimeUtc.Ticks);
        }

        return snapshot;
    }

    /// <summary>
    /// Compares the current workspace state against a baseline snapshot and returns changed file paths.
    /// </summary>
    /// <param name="workspaceRoot">The root directory of the workspace.</param>
    /// <param name="baseline">The baseline snapshot to compare against.</param>
    /// <returns>A list of relative paths that were created, modified, or deleted since the baseline.</returns>
    public static IReadOnlyList<string> DetectChanges(
        string workspaceRoot,
        IReadOnlyDictionary<string, (long Length, long LastWriteUtcTicks)> baseline)
    {
        Dictionary<string, (long Length, long LastWriteUtcTicks)> current = CaptureSnapshot(workspaceRoot);
        HashSet<string> changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string currentPath in current.Keys.Where(p => !baseline.ContainsKey(p)))
        {
            changed.Add(currentPath);
        }

        foreach (KeyValuePair<string, (long Length, long LastWriteUtcTicks)> entry in current
                     .Where(entry => baseline.TryGetValue(entry.Key, out (long Length, long LastWriteUtcTicks) baselineSignature)
                                     && baselineSignature != entry.Value))
        {
            changed.Add(entry.Key);
        }

        foreach (string baselinePath in baseline.Keys.Where(p => !current.ContainsKey(p)))
        {
            changed.Add(baselinePath);
        }

        return changed.ToArray();
    }

    /// <summary>
    /// Determines whether a relative path should be excluded from workspace snapshots.
    /// </summary>
    /// <param name="relativePath">The relative path to check.</param>
    /// <returns><c>true</c> if the path should be ignored; otherwise <c>false</c>.</returns>
    public static bool IsIgnoredPath(string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/');
        return normalized.StartsWith(".git/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(".agent-harness/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("node_modules/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("bin/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("obj/", StringComparison.OrdinalIgnoreCase);
    }

    internal static IReadOnlyList<string> EnumerateSnapshotFiles(string workspaceRoot)
    {
        string normalizedRoot = NormalizeRoot(workspaceRoot);
        if (TryGetGitVisibleFiles(normalizedRoot, out IReadOnlyList<string> gitVisibleFiles))
        {
            return gitVisibleFiles;
        }

        return EnumerateFileSystemFiles(normalizedRoot).ToArray();
    }

    private static string NormalizeRoot(string workspaceRoot)
        => Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool TryGetGitVisibleFiles(string workspaceRoot, out IReadOnlyList<string> relativePaths)
    {
        relativePaths = Array.Empty<string>();
        try
        {
            string? repositoryPath = Repository.Discover(workspaceRoot);
            if (string.IsNullOrWhiteSpace(repositoryPath))
            {
                return false;
            }

            using Repository repository = new Repository(repositoryPath);
            relativePaths = EnumerateGitVisibleFiles(repository, workspaceRoot).ToArray();
            return true;
        }
        catch (Exception ex) when (ex is RepositoryNotFoundException or LibGit2SharpException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static IEnumerable<string> EnumerateGitVisibleFiles(Repository repository, string workspaceRoot)
    {
        string repositoryRoot = NormalizeRoot(repository.Info.WorkingDirectory);
        string normalizedWorkspaceRoot = NormalizeRoot(workspaceRoot);
        HashSet<string> output = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (IndexEntry entry in repository.Index)
        {
            AddRepositoryRelativePath(entry.Path, repositoryRoot, normalizedWorkspaceRoot, output);
        }

        RepositoryStatus status = repository.RetrieveStatus(new StatusOptions
        {
            IncludeUntracked = true,
            RecurseUntrackedDirs = true
        });

        foreach (StatusEntry entry in status.Where(entry => entry.State.HasFlag(FileStatus.NewInWorkdir)))
        {
            AddRepositoryRelativePath(entry.FilePath, repositoryRoot, normalizedWorkspaceRoot, output);
        }

        return output.OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    private static void AddRepositoryRelativePath(string repositoryRelativePath, string repositoryRoot, string workspaceRoot, ISet<string> output)
    {
        string fullPath = Path.GetFullPath(Path.Combine(repositoryRoot, repositoryRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsUnderRoot(fullPath, workspaceRoot) || !File.Exists(fullPath))
        {
            return;
        }

        string workspaceRelativePath = Path.GetRelativePath(workspaceRoot, fullPath);
        if (!IsIgnoredPath(workspaceRelativePath))
        {
            output.Add(workspaceRelativePath);
        }
    }

    internal static bool IsUnderRoot(string fullPath, string rootPath)
        => fullPath.Equals(rootPath, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(rootPath + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> EnumerateFileSystemFiles(string workspaceRoot)
    {
        Stack<string> pendingDirectories = new Stack<string>();
        pendingDirectories.Push(workspaceRoot);

        while (pendingDirectories.Count > 0)
        {
            string currentDirectory = pendingDirectories.Pop();
            foreach (string directory in EnumerateDirectories(currentDirectory))
            {
                string relativeDirectory = Path.GetRelativePath(workspaceRoot, directory);
                if (!IsIgnoredPath(relativeDirectory + Path.DirectorySeparatorChar))
                {
                    pendingDirectories.Push(directory);
                }
            }

            foreach (string file in EnumerateFiles(currentDirectory))
            {
                string relativeFile = Path.GetRelativePath(workspaceRoot, file);
                if (!IsIgnoredPath(relativeFile))
                {
                    yield return relativeFile;
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateDirectories(string directory)
    {
        try
        {
            return Directory.EnumerateDirectories(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<string> EnumerateFiles(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

}
