namespace ArchHarness.App.Workspace;

public class FileSystemWorkspaceAdapter : IWorkspaceAdapter
{
    private Dictionary<string, FileSignature> _baselineSnapshot = new(StringComparer.OrdinalIgnoreCase);

    public string RootPath { get; private set; }

    public FileSystemWorkspaceAdapter(string rootPath)
    {
        RootPath = Path.GetFullPath(rootPath);
    }

    public virtual Task InitializeAsync(string? projectName, bool initGit, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(RootPath);
        if (!string.IsNullOrWhiteSpace(projectName))
        {
            RootPath = Path.Combine(RootPath, projectName);
            Directory.CreateDirectory(RootPath);
        }

        if (initGit)
        {
            Directory.CreateDirectory(Path.Combine(RootPath, ".git"));
        }

        _baselineSnapshot = BuildSnapshot();

        return Task.CompletedTask;
    }

    public async Task WriteTextAsync(string relativePath, string content, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(Path.Combine(RootPath, relativePath));
        if (!fullPath.StartsWith(RootPath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Write attempted outside workspace root.");
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(fullPath, content, cancellationToken);
    }

    public Task<string> DiffAsync(CancellationToken cancellationToken)
    {
        var content = ComputeChangedPathsSinceBaseline()
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult(string.Join(Environment.NewLine, content));
    }

    protected IReadOnlyCollection<string> ComputeChangedPathsSinceBaseline()
    {
        var currentSnapshot = BuildSnapshot();
        var changedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach ((var path, var signature) in currentSnapshot)
        {
            if (!_baselineSnapshot.TryGetValue(path, out var baselineSignature) || !signature.Equals(baselineSignature))
            {
                changedPaths.Add(path);
            }
        }

        foreach (var baselinePath in _baselineSnapshot.Keys)
        {
            if (!currentSnapshot.ContainsKey(baselinePath))
            {
                changedPaths.Add(baselinePath);
            }
        }

        return changedPaths;
    }

    private Dictionary<string, FileSignature> BuildSnapshot()
    {
        var snapshot = new Dictionary<string, FileSignature>(StringComparer.OrdinalIgnoreCase);
        foreach (var filePath in Directory.GetFiles(RootPath, "*", SearchOption.AllDirectories))
        {
            if (IsExcludedPath(filePath))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(RootPath, filePath);
            var info = new FileInfo(filePath);
            snapshot[relativePath] = new FileSignature(info.Length, info.LastWriteTimeUtc.Ticks);
        }

        return snapshot;
    }

    private bool IsExcludedPath(string fullPath)
    {
        var relativePath = Path.GetRelativePath(RootPath, fullPath);
        var normalized = relativePath.Replace('\\', '/');

        return normalized.StartsWith(".git/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("bin/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("obj/", StringComparison.OrdinalIgnoreCase);
    }

    private readonly record struct FileSignature(long Length, long LastWriteUtcTicks);
}
