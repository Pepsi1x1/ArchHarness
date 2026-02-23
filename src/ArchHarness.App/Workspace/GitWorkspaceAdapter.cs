namespace ArchHarness.App.Workspace;

public sealed class GitWorkspaceAdapter : FileSystemWorkspaceAdapter
{
    public GitWorkspaceAdapter(string rootPath) : base(rootPath)
    {
    }

    public override Task InitializeAsync(string? projectName, bool initGit, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(Path.Combine(RootPath, ".git")) && !initGit)
        {
            throw new InvalidOperationException("existing-git mode requires a .git directory.");
        }

        return base.InitializeAsync(projectName, initGit: true, cancellationToken);
    }
}
