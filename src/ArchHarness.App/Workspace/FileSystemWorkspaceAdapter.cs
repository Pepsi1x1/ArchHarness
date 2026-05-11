using ArchHarness.App.Core;

namespace ArchHarness.App.Workspace;

/// <summary>
/// File-system-backed workspace adapter that tracks changes via file snapshots.
/// </summary>
public class FileSystemWorkspaceAdapter : IWorkspaceAdapter
{
    private Dictionary<string, FileSignature> _baselineSnapshot = new Dictionary<string, FileSignature>(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public string RootPath { get; private set; }

    /// <summary>
    /// Initializes a new instance of <see cref="FileSystemWorkspaceAdapter"/> for the specified root path.
    /// </summary>
    /// <param name="rootPath">The workspace root directory path.</param>
    public FileSystemWorkspaceAdapter(string rootPath)
    {
        this.RootPath = Path.GetFullPath(rootPath);
    }

    /// <inheritdoc />
    public virtual Task InitializeAsync(string? projectName, bool initGit, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(this.RootPath);
        if (!string.IsNullOrWhiteSpace(projectName))
        {
            this.RootPath = Path.Combine(this.RootPath, projectName);
            Directory.CreateDirectory(this.RootPath);
        }

        if (initGit)
        {
            Directory.CreateDirectory(Path.Combine(this.RootPath, ".git"));
        }

        this._baselineSnapshot = this.BuildSnapshot();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task WriteTextAsync(string relativePath, string content, CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(Path.Combine(this.RootPath, relativePath));
        if (!fullPath.StartsWith(this.RootPath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Write attempted outside workspace root.");
        }

        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(fullPath, content, cancellationToken);
    }

    /// <inheritdoc />
    public virtual Task<string> DiffAsync(CancellationToken cancellationToken)
    {
        string[] content = this.ComputeChangedPathsSinceBaseline()
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult(string.Join(Environment.NewLine, content));
    }

    /// <summary>
    /// Computes the set of relative paths that have changed since the baseline snapshot was taken.
    /// </summary>
    /// <returns>A collection of changed relative file paths.</returns>
    protected IReadOnlyCollection<string> ComputeChangedPathsSinceBaseline()
    {
        Dictionary<string, FileSignature> currentSnapshot = this.BuildSnapshot();
        HashSet<string> changedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (KeyValuePair<string, FileSignature> entry in currentSnapshot
                     .Where(entry => !this._baselineSnapshot.TryGetValue(entry.Key, out FileSignature baselineSignature)
                                     || !entry.Value.Equals(baselineSignature)))
        {
            changedPaths.Add(entry.Key);
        }

        foreach (string baselinePath in this._baselineSnapshot.Keys.Where(baselinePath => !currentSnapshot.ContainsKey(baselinePath)))
        {
            changedPaths.Add(baselinePath);
        }

        return changedPaths;
    }

    private Dictionary<string, FileSignature> BuildSnapshot()
    {
        Dictionary<string, FileSignature> snapshot = new Dictionary<string, FileSignature>(StringComparer.OrdinalIgnoreCase);
        foreach (string relativePath in WorkspaceSnapshotHelper.EnumerateSnapshotFiles(this.RootPath))
        {
            string filePath = Path.GetFullPath(Path.Combine(this.RootPath, relativePath));
            if (TryGetFileSignature(filePath, out FileSignature signature))
            {
                snapshot[relativePath] = signature;
            }
        }

        return snapshot;
    }

    private static bool TryGetFileSignature(string filePath, out FileSignature signature)
    {
        try
        {
            FileInfo info = new FileInfo(filePath);
            signature = new FileSignature(info.Length, info.LastWriteTimeUtc.Ticks);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            signature = default;
            return false;
        }
    }

    private readonly record struct FileSignature(long Length, long LastWriteUtcTicks);
}
