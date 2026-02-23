namespace ArchHarness.App.Workspace;

public class FileSystemWorkspaceAdapter : IWorkspaceAdapter
{
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
        var content = Directory.GetFiles(RootPath, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(RootPath, f))
            .OrderBy(f => f)
            .ToArray();
        return Task.FromResult(string.Join(Environment.NewLine, content));
    }
}
