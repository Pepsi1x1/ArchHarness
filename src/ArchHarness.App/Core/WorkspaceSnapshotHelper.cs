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
        foreach (string fullPath in Directory
                     .GetFiles(workspaceRoot, "*", SearchOption.AllDirectories)
                     .Where(fullPath => !IsIgnoredPath(Path.GetRelativePath(workspaceRoot, fullPath))))
        {
            string relativePath = Path.GetRelativePath(workspaceRoot, fullPath);
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
            || normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("bin/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("obj/", StringComparison.OrdinalIgnoreCase);
    }
}
