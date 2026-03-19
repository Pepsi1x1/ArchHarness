using LibGit2Sharp;

namespace ArchHarness.App.SourceControl;

/// <summary>
/// Provides Git repository metadata using LibGit2Sharp.
/// </summary>
public sealed class LibGit2SharpRepositoryInfoService : IGitRepositoryInfoService
{
    private const string FailureCodeNotGitRepository = "not-git-repository";
    private const string FailureCodeBranchNotFound = "branch-not-found";
    private const string FailureCodeDirtyWorktree = "dirty-worktree";
    private const string FailureCodeCheckoutConflict = "checkout-conflict";
    private const string FailureCodeInvalidRequest = "invalid-request";
    private const string NotGitRepositoryMessage = "The selected project is not a Git repository.";

    /// <inheritdoc />
    public GitRepositoryBranchInfo GetBranchInfo(string workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            return new GitRepositoryBranchInfo(false, null, Array.Empty<string>());
        }

        try
        {
            string fullWorkspacePath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(workspacePath));
            string? discoveredPath = Repository.Discover(fullWorkspacePath);
            if (string.IsNullOrWhiteSpace(discoveredPath))
            {
                return new GitRepositoryBranchInfo(false, null, Array.Empty<string>());
            }

            using Repository repository = new Repository(discoveredPath);
            string? currentBranch = repository.Head?.FriendlyName;
            List<string> branchNames = repository.Branches
                .Where(branch => !branch.IsRemote)
                .Select(branch => branch.FriendlyName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!string.IsNullOrWhiteSpace(currentBranch)
                && !branchNames.Contains(currentBranch, StringComparer.OrdinalIgnoreCase))
            {
                branchNames.Insert(0, currentBranch);
            }

            return new GitRepositoryBranchInfo(true, currentBranch, branchNames);
        }
        catch (RepositoryNotFoundException)
        {
            return new GitRepositoryBranchInfo(false, null, Array.Empty<string>());
        }
        catch (LibGit2SharpException)
        {
            return new GitRepositoryBranchInfo(false, null, Array.Empty<string>());
        }
        catch (IOException)
        {
            return new GitRepositoryBranchInfo(false, null, Array.Empty<string>());
        }
        catch (UnauthorizedAccessException)
        {
            return new GitRepositoryBranchInfo(false, null, Array.Empty<string>());
        }
    }

    /// <inheritdoc />
    public GitBranchCheckoutResult CheckoutBranch(string workspacePath, string branchName)
    {
        if (string.IsNullOrWhiteSpace(branchName))
        {
            return new GitBranchCheckoutResult(
                false,
                FailureCodeInvalidRequest,
                "Branch name is required.",
                GetBranchInfo(workspacePath));
        }

        try
        {
            string fullWorkspacePath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(workspacePath));
            string? discoveredPath = Repository.Discover(fullWorkspacePath);
            if (string.IsNullOrWhiteSpace(discoveredPath))
            {
                return new GitBranchCheckoutResult(
                    false,
                    FailureCodeNotGitRepository,
                    NotGitRepositoryMessage,
                    new GitRepositoryBranchInfo(false, null, Array.Empty<string>()));
            }

            using Repository repository = new Repository(discoveredPath);
            if (string.Equals(repository.Head?.FriendlyName, branchName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return new GitBranchCheckoutResult(true, null, null, BuildBranchInfo(repository));
            }

            Branch? branch = repository.Branches[branchName.Trim()];
            if (branch is null || branch.IsRemote)
            {
                return new GitBranchCheckoutResult(
                    false,
                    FailureCodeBranchNotFound,
                    $"Local branch '{branchName.Trim()}' was not found.",
                    BuildBranchInfo(repository));
            }

            RepositoryStatus status = repository.RetrieveStatus();
            if (status.IsDirty)
            {
                return new GitBranchCheckoutResult(
                    false,
                    FailureCodeDirtyWorktree,
                    "Cannot switch branches while the working tree has uncommitted changes.",
                    BuildBranchInfo(repository));
            }

            Commands.Checkout(repository, branch);
            return new GitBranchCheckoutResult(true, null, null, BuildBranchInfo(repository));
        }
        catch (CheckoutConflictException)
        {
            return new GitBranchCheckoutResult(
                false,
                FailureCodeCheckoutConflict,
                "Git could not switch branches because the checkout would overwrite local changes.",
                GetBranchInfo(workspacePath));
        }
        catch (RepositoryNotFoundException)
        {
            return new GitBranchCheckoutResult(
                false,
                FailureCodeNotGitRepository,
                NotGitRepositoryMessage,
                new GitRepositoryBranchInfo(false, null, Array.Empty<string>()));
        }
        catch (LibGit2SharpException ex)
        {
            return new GitBranchCheckoutResult(
                false,
                FailureCodeCheckoutConflict,
                ex.Message,
                GetBranchInfo(workspacePath));
        }
        catch (IOException)
        {
            return new GitBranchCheckoutResult(
                false,
                FailureCodeCheckoutConflict,
                "Git could not switch branches because the repository files are not currently accessible.",
                GetBranchInfo(workspacePath));
        }
        catch (UnauthorizedAccessException)
        {
            return new GitBranchCheckoutResult(
                false,
                FailureCodeCheckoutConflict,
                "Git could not switch branches because the repository files are not currently accessible.",
                GetBranchInfo(workspacePath));
        }
    }

    /// <inheritdoc />
    public GitWorkingTreeStatus GetWorkingTreeStatus(string workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            return new GitWorkingTreeStatus(false, null, false, Array.Empty<GitWorkingTreeFileChange>());
        }

        try
        {
            using Repository repository = OpenRepository(workspacePath);
            return BuildWorkingTreeStatus(repository);
        }
        catch (RepositoryNotFoundException)
        {
            return new GitWorkingTreeStatus(false, null, false, Array.Empty<GitWorkingTreeFileChange>());
        }
        catch (LibGit2SharpException)
        {
            return new GitWorkingTreeStatus(false, null, false, Array.Empty<GitWorkingTreeFileChange>());
        }
        catch (IOException)
        {
            return new GitWorkingTreeStatus(false, null, false, Array.Empty<GitWorkingTreeFileChange>());
        }
        catch (UnauthorizedAccessException)
        {
            return new GitWorkingTreeStatus(false, null, false, Array.Empty<GitWorkingTreeFileChange>());
        }
    }

    /// <inheritdoc />
    public GitWorkingTreeDiffResult GetWorkingTreeDiff(string workspacePath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return new GitWorkingTreeDiffResult(false, false, null, null, "A repository-relative path is required.");
        }

        try
        {
            using Repository repository = OpenRepository(workspacePath);
            string normalizedPath = NormalizeRepositoryPath(relativePath);
            Patch patch = repository.Diff.Compare<Patch>(repository.Head.Tip?.Tree, DiffTargets.Index | DiffTargets.WorkingDirectory, new[] { normalizedPath });
            PatchEntryChanges? change = patch.FirstOrDefault(entry =>
                string.Equals(NormalizeRepositoryPath(entry.Path), normalizedPath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(NormalizeRepositoryPath(entry.OldPath), normalizedPath, StringComparison.OrdinalIgnoreCase));

            if (change is null)
            {
                return new GitWorkingTreeDiffResult(true, false, normalizedPath, null, "No diff is available for the selected file.");
            }

            string diffText = string.IsNullOrWhiteSpace(change.Patch)
                ? "No textual diff is available for the selected file."
                : change.Patch;

            return new GitWorkingTreeDiffResult(true, true, normalizedPath, diffText, null);
        }
        catch (RepositoryNotFoundException)
        {
            return new GitWorkingTreeDiffResult(false, false, null, null, "The selected project is not a Git repository.");
        }
        catch (LibGit2SharpException ex)
        {
            return new GitWorkingTreeDiffResult(true, false, NormalizeRepositoryPath(relativePath), null, ex.Message);
        }
        catch (IOException)
        {
            return new GitWorkingTreeDiffResult(true, false, NormalizeRepositoryPath(relativePath), null, "Git could not read the selected diff because the repository files are not currently accessible.");
        }
        catch (UnauthorizedAccessException)
        {
            return new GitWorkingTreeDiffResult(true, false, NormalizeRepositoryPath(relativePath), null, "Git could not read the selected diff because the repository files are not currently accessible.");
        }
    }

    private static GitRepositoryBranchInfo BuildBranchInfo(Repository repository)
    {
        string? currentBranch = repository.Head?.FriendlyName;
        List<string> branchNames = repository.Branches
            .Where(branch => !branch.IsRemote)
            .Select(branch => branch.FriendlyName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!string.IsNullOrWhiteSpace(currentBranch)
            && !branchNames.Contains(currentBranch, StringComparer.OrdinalIgnoreCase))
        {
            branchNames.Insert(0, currentBranch);
        }

        return new GitRepositoryBranchInfo(true, currentBranch, branchNames);
    }

    private static GitWorkingTreeStatus BuildWorkingTreeStatus(Repository repository)
    {
        string? currentBranch = repository.Head?.FriendlyName;
        RepositoryStatus status = repository.RetrieveStatus(new StatusOptions
        {
            IncludeUnaltered = false,
            RecurseUntrackedDirs = true
        });

        List<GitWorkingTreeFileChange> files = status
            .Where(entry => entry.State != FileStatus.Unaltered && entry.State != FileStatus.Ignored)
            .Select(entry => new GitWorkingTreeFileChange(
                NormalizeRepositoryPath(entry.FilePath),
                DescribeWorkingTreeStatus(entry.State),
                null,
                IsStaged(entry.State),
                IsUntracked(entry.State)))
            .OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new GitWorkingTreeStatus(true, currentBranch, files.Count > 0, files);
    }

    private static Repository OpenRepository(string workspacePath)
    {
        string fullWorkspacePath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(workspacePath));
        string? discoveredPath = Repository.Discover(fullWorkspacePath);
        if (string.IsNullOrWhiteSpace(discoveredPath))
        {
            throw new RepositoryNotFoundException("The selected project is not a Git repository.");
        }

        return new Repository(discoveredPath);
    }

    private static string NormalizeRepositoryPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        string normalized = path.Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized.TrimStart('/');
    }

    private static string DescribeWorkingTreeStatus(FileStatus state)
    {
        if (state.HasFlag(FileStatus.Conflicted))
        {
            return "Conflicted";
        }

        if (state.HasFlag(FileStatus.RenamedInIndex) || state.HasFlag(FileStatus.RenamedInWorkdir))
        {
            return "Renamed";
        }

        if (state.HasFlag(FileStatus.NewInIndex) || state.HasFlag(FileStatus.NewInWorkdir))
        {
            return "Added";
        }

        if (state.HasFlag(FileStatus.DeletedFromIndex) || state.HasFlag(FileStatus.DeletedFromWorkdir))
        {
            return "Deleted";
        }

        if (state.HasFlag(FileStatus.TypeChangeInIndex) || state.HasFlag(FileStatus.TypeChangeInWorkdir))
        {
            return "Type changed";
        }

        return "Modified";
    }

    private static bool IsStaged(FileStatus state)
    {
        return state.HasFlag(FileStatus.NewInIndex)
            || state.HasFlag(FileStatus.ModifiedInIndex)
            || state.HasFlag(FileStatus.DeletedFromIndex)
            || state.HasFlag(FileStatus.RenamedInIndex)
            || state.HasFlag(FileStatus.TypeChangeInIndex);
    }

    private static bool IsUntracked(FileStatus state)
    {
        return state.HasFlag(FileStatus.NewInWorkdir)
            && !state.HasFlag(FileStatus.NewInIndex);
    }
}
