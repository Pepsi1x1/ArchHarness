namespace ArchHarness.App.Workspace;

public interface IWorkspaceAdapter
{
    string RootPath { get; }
    Task InitializeAsync(string? projectName, bool initGit, CancellationToken cancellationToken);
    Task WriteTextAsync(string relativePath, string content, CancellationToken cancellationToken);
    Task<string> DiffAsync(CancellationToken cancellationToken);
}

public static class WorkspaceAdapterFactory
{
    public static IWorkspaceAdapter Create(string mode, string rootPath)
        => mode switch
        {
            "existing-git" => new GitWorkspaceAdapter(rootPath),
            "existing-folder" => new FileSystemWorkspaceAdapter(rootPath),
            "new-project" => new FileSystemWorkspaceAdapter(rootPath),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), $"Unsupported workspace mode: {mode}")
        };
}
