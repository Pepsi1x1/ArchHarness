using ArchHarness.App.Core;
using LibGit2Sharp;

namespace ArchHarness.App.Workspace;

/// <summary>
/// Git-backed workspace adapter that combines git status with file-system snapshot diffing.
/// </summary>
public sealed class GitWorkspaceAdapter : FileSystemWorkspaceAdapter
{
    private HashSet<string> _initialChangedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of <see cref="GitWorkspaceAdapter"/> for the specified root path.
    /// </summary>
    /// <param name="rootPath">The workspace root directory path.</param>
    public GitWorkspaceAdapter(string rootPath) : base(rootPath)
    {
    }

    /// <inheritdoc />
    public override async Task InitializeAsync(string? projectName, bool initGit, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(Path.Combine(this.RootPath, ".git")) && !initGit)
        {
            throw new InvalidOperationException("existing-git mode requires a .git directory.");
        }

        await base.InitializeAsync(projectName, initGit: true, cancellationToken);
        this._initialChangedPaths = new HashSet<string>(await this.GetGitChangedPathsAsync(cancellationToken), StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public override async Task<string> DiffAsync(CancellationToken cancellationToken)
    {
        HashSet<string> gitChangedPaths = new HashSet<string>(await this.GetGitChangedPathsAsync(cancellationToken), StringComparer.OrdinalIgnoreCase);
        HashSet<string> snapshotChangedPaths = new HashSet<string>(this.ComputeChangedPathsSinceBaseline(), StringComparer.OrdinalIgnoreCase);

        // Exclude files that were already dirty at startup unless they changed since baseline.
        gitChangedPaths.ExceptWith(this._initialChangedPaths);
        gitChangedPaths.UnionWith(snapshotChangedPaths);

        return string.Join(
            Environment.NewLine,
            gitChangedPaths
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
    }

    private async Task<IReadOnlyCollection<string>> GetGitChangedPathsAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        HashSet<string> changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? repositoryPath = Repository.Discover(this.RootPath);
        if (string.IsNullOrWhiteSpace(repositoryPath))
        {
            return changed;
        }

        using Repository repository = new Repository(repositoryPath);
        string repositoryRoot = Path.GetFullPath(repository.Info.WorkingDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string workspaceRoot = Path.GetFullPath(this.RootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        RepositoryStatus status = repository.RetrieveStatus(new StatusOptions
        {
            IncludeUntracked = true,
            RecurseUntrackedDirs = true
        });

        foreach (StatusEntry entry in status.Where(entry => entry.State != FileStatus.Unaltered && !entry.State.HasFlag(FileStatus.Ignored)))
        {
            AddRepositoryRelativePath(changed, entry.FilePath, repositoryRoot, workspaceRoot);
        }

        return changed;
    }

    private static void AddRepositoryRelativePath(ISet<string> output, string repositoryRelativePath, string repositoryRoot, string workspaceRoot)
    {
        string fullPath = Path.GetFullPath(Path.Combine(repositoryRoot, repositoryRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!WorkspaceSnapshotHelper.IsUnderRoot(fullPath, workspaceRoot))
        {
            return;
        }

        string workspaceRelativePath = Path.GetRelativePath(workspaceRoot, fullPath);
        if (!WorkspaceSnapshotHelper.IsIgnoredPath(workspaceRelativePath))
        {
            output.Add(workspaceRelativePath);
        }
    }
}
