using System.Text;
using LibGit2Sharp;

namespace ArchHarness.App.SourceControl;

/// <summary>
/// Provides Git repository metadata using LibGit2Sharp.
/// </summary>
public sealed class LibGit2SharpRepositoryInfoService : IGitRepositoryInfoService
{
    private const string FAILURE_CODE_NOT_GIT_REPOSITORY = "not-git-repository";
    private const string FAILURE_CODE_BRANCH_NOT_FOUND = "branch-not-found";
    private const string FAILURE_CODE_CLONE_FAILED = "clone-failed";
    private const string FAILURE_CODE_ALREADY_GIT_REPOSITORY = "already-git-repository";
    private const string FAILURE_CODE_DIRTY_WORKTREE = "dirty-worktree";
    private const string FAILURE_CODE_CHECKOUT_CONFLICT = "checkout-conflict";
    private const string FAILURE_CODE_INVALID_REQUEST = "invalid-request";
    private const string FAILURE_CODE_NO_CHANGES = "no-changes";
    private const string FAILURE_CODE_STASH_FAILED = "stash-failed";
    private const string FAILURE_CODE_BRANCH_ALREADY_EXISTS = "branch-already-exists";
    private const string FAILURE_CODE_BRANCH_CREATE_FAILED = "branch-create-failed";
    private const string FAILURE_CODE_COMMIT_FAILED = "commit-failed";
    private const string FAILURE_CODE_MERGE_CONFLICT = "merge-conflict";
    private const string FAILURE_CODE_MERGE_FAILED = "merge-failed";
    private const string NOT_GIT_REPOSITORY_MESSAGE = "The selected project is not a Git repository.";

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
    public GitBranchCheckoutResult CheckoutBranch(string workspacePath, string branchName, GitAuthenticationOptions? authentication = null)
    {
        if (string.IsNullOrWhiteSpace(branchName))
        {
            return new GitBranchCheckoutResult(
                false,
                FAILURE_CODE_INVALID_REQUEST,
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
                    FAILURE_CODE_NOT_GIT_REPOSITORY,
                    NOT_GIT_REPOSITORY_MESSAGE,
                    new GitRepositoryBranchInfo(false, null, Array.Empty<string>()));
            }

            using Repository repository = new Repository(discoveredPath);
            string normalizedBranchName = branchName.Trim();
            if (string.Equals(repository.Head?.FriendlyName, normalizedBranchName, StringComparison.OrdinalIgnoreCase))
            {
                return new GitBranchCheckoutResult(true, null, null, BuildBranchInfo(repository));
            }

            Branch? branch = ResolveLocalBranch(repository, normalizedBranchName);
            if (branch is null && authentication is not null)
            {
                FetchAllRemotes(repository, authentication);
                branch = ResolveLocalBranch(repository, normalizedBranchName);
            }

            if (branch is null || branch.IsRemote)
            {
                return new GitBranchCheckoutResult(
                    false,
                    FAILURE_CODE_BRANCH_NOT_FOUND,
                    $"Branch '{normalizedBranchName}' was not found locally or on a configured remote.",
                    BuildBranchInfo(repository));
            }

            RepositoryStatus status = repository.RetrieveStatus();
            if (status.IsDirty)
            {
                return new GitBranchCheckoutResult(
                    false,
                    FAILURE_CODE_DIRTY_WORKTREE,
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
                FAILURE_CODE_CHECKOUT_CONFLICT,
                "Git could not switch branches because the checkout would overwrite local changes.",
                GetBranchInfo(workspacePath));
        }
        catch (RepositoryNotFoundException)
        {
            return new GitBranchCheckoutResult(
                false,
                FAILURE_CODE_NOT_GIT_REPOSITORY,
                NOT_GIT_REPOSITORY_MESSAGE,
                new GitRepositoryBranchInfo(false, null, Array.Empty<string>()));
        }
        catch (LibGit2SharpException ex)
        {
            return new GitBranchCheckoutResult(
                false,
                FAILURE_CODE_CHECKOUT_CONFLICT,
                ex.Message,
                GetBranchInfo(workspacePath));
        }
        catch (IOException)
        {
            return new GitBranchCheckoutResult(
                false,
                FAILURE_CODE_CHECKOUT_CONFLICT,
                "Git could not switch branches because the repository files are not currently accessible.",
                GetBranchInfo(workspacePath));
        }
        catch (UnauthorizedAccessException)
        {
            return new GitBranchCheckoutResult(
                false,
                FAILURE_CODE_CHECKOUT_CONFLICT,
                "Git could not switch branches because the repository files are not currently accessible.",
                GetBranchInfo(workspacePath));
        }
    }

    /// <inheritdoc />
    public GitCloneResult CloneRepository(string workspacePath, string remoteUrl, string? branchName = null, GitAuthenticationOptions? authentication = null)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            return new GitCloneResult(
                false,
                FAILURE_CODE_INVALID_REQUEST,
                "Workspace path is required.",
                new GitRepositoryBranchInfo(false, null, Array.Empty<string>()));
        }

        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            return new GitCloneResult(
                false,
                FAILURE_CODE_INVALID_REQUEST,
                "Repository clone URL is required.",
                GetBranchInfo(workspacePath));
        }

        try
        {
            string fullWorkspacePath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(workspacePath));
            string? discoveredPath = Repository.Discover(fullWorkspacePath);
            if (!string.IsNullOrWhiteSpace(discoveredPath))
            {
                return new GitCloneResult(
                    false,
                    FAILURE_CODE_ALREADY_GIT_REPOSITORY,
                    "The selected folder already contains a Git repository.",
                    GetBranchInfo(workspacePath));
            }

            string? parentDirectory = Path.GetDirectoryName(fullWorkspacePath);
            if (!string.IsNullOrWhiteSpace(parentDirectory))
            {
                Directory.CreateDirectory(parentDirectory);
            }

            CloneOptions cloneOptions = new CloneOptions();
            LibGit2Sharp.Handlers.CredentialsHandler? credentialsProvider = BuildCredentialsProvider(authentication);
            if (credentialsProvider is not null)
            {
                cloneOptions.FetchOptions.CredentialsProvider = credentialsProvider;
            }

            Repository.Clone(remoteUrl.Trim(), fullWorkspacePath, cloneOptions);

            using Repository repository = new Repository(fullWorkspacePath);
            if (!string.IsNullOrWhiteSpace(branchName))
            {
                Branch? branch = ResolveLocalBranch(repository, branchName.Trim());
                if (branch is null)
                {
                    return new GitCloneResult(
                        false,
                        FAILURE_CODE_BRANCH_NOT_FOUND,
                        $"Branch '{branchName.Trim()}' was not found after cloning the repository.",
                        BuildBranchInfo(repository));
                }

                Commands.Checkout(repository, branch);
            }

            return new GitCloneResult(true, null, null, BuildBranchInfo(repository));
        }
        catch (LibGit2SharpException ex)
        {
            return new GitCloneResult(false, FAILURE_CODE_CLONE_FAILED, ex.Message, GetBranchInfo(workspacePath));
        }
        catch (IOException)
        {
            return new GitCloneResult(false, FAILURE_CODE_CLONE_FAILED, "Git could not clone the repository because the target folder is not currently accessible.", GetBranchInfo(workspacePath));
        }
        catch (UnauthorizedAccessException)
        {
            return new GitCloneResult(false, FAILURE_CODE_CLONE_FAILED, "Git could not clone the repository because the target folder is not currently accessible.", GetBranchInfo(workspacePath));
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

            StatusEntry? statusEntry = repository.RetrieveStatus(new StatusOptions
            {
                IncludeUnaltered = false,
                RecurseUntrackedDirs = true
            }).FirstOrDefault(entry =>
                string.Equals(NormalizeRepositoryPath(entry.FilePath), normalizedPath, StringComparison.OrdinalIgnoreCase));

            if (change is null)
            {
                string? addedFileDiff = TryBuildAddedFileDiff(repository, normalizedPath, statusEntry?.State);
                if (!string.IsNullOrWhiteSpace(addedFileDiff))
                {
                    return new GitWorkingTreeDiffResult(true, true, normalizedPath, addedFileDiff, null);
                }

                return new GitWorkingTreeDiffResult(true, false, normalizedPath, null, "No diff is available for the selected file.");
            }

            string? diffText = string.IsNullOrWhiteSpace(change.Patch)
                ? TryBuildAddedFileDiff(repository, normalizedPath, statusEntry?.State)
                : change.Patch;

            diffText ??= "No textual diff is available for the selected file.";

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

    /// <inheritdoc />
    public GitStashChangesResult StashWorkingTreeChanges(string workspacePath, string? message)
    {
        try
        {
            using Repository repository = OpenRepository(workspacePath);
            GitWorkingTreeStatus workingTreeStatus = BuildWorkingTreeStatus(repository);
            if (!workingTreeStatus.HasChanges)
            {
                GitRepositoryBranchInfo branchInfo = BuildBranchInfo(repository);
                return new GitStashChangesResult(false, FAILURE_CODE_NO_CHANGES, "There are no local changes to stash.", branchInfo, workingTreeStatus);
            }

            Signature signature = BuildStashSignature(repository);
            string stashMessage = string.IsNullOrWhiteSpace(message)
                ? $"ArchHarness stash before switching branches from {repository.Head?.FriendlyName ?? "current branch"}"
                : message.Trim();

            repository.Stashes.Add(signature, stashMessage, StashModifiers.IncludeUntracked);

            GitRepositoryBranchInfo updatedBranchInfo = BuildBranchInfo(repository);
            GitWorkingTreeStatus updatedWorkingTreeStatus = BuildWorkingTreeStatus(repository);
            return new GitStashChangesResult(true, null, null, updatedBranchInfo, updatedWorkingTreeStatus);
        }
        catch (RepositoryNotFoundException)
        {
            return new GitStashChangesResult(
                false,
                FAILURE_CODE_NOT_GIT_REPOSITORY,
                NOT_GIT_REPOSITORY_MESSAGE,
                new GitRepositoryBranchInfo(false, null, Array.Empty<string>()),
                new GitWorkingTreeStatus(false, null, false, Array.Empty<GitWorkingTreeFileChange>()));
        }
        catch (LibGit2SharpException ex)
        {
            GitRepositoryBranchInfo branchInfo = GetBranchInfo(workspacePath);
            GitWorkingTreeStatus workingTreeStatus = GetWorkingTreeStatus(workspacePath);
            return new GitStashChangesResult(false, FAILURE_CODE_STASH_FAILED, ex.Message, branchInfo, workingTreeStatus);
        }
        catch (IOException)
        {
            GitRepositoryBranchInfo branchInfo = GetBranchInfo(workspacePath);
            GitWorkingTreeStatus workingTreeStatus = GetWorkingTreeStatus(workspacePath);
            return new GitStashChangesResult(false, FAILURE_CODE_STASH_FAILED, "Git could not create the stash because the repository files are not currently accessible.", branchInfo, workingTreeStatus);
        }
        catch (UnauthorizedAccessException)
        {
            GitRepositoryBranchInfo branchInfo = GetBranchInfo(workspacePath);
            GitWorkingTreeStatus workingTreeStatus = GetWorkingTreeStatus(workspacePath);
            return new GitStashChangesResult(false, FAILURE_CODE_STASH_FAILED, "Git could not create the stash because the repository files are not currently accessible.", branchInfo, workingTreeStatus);
        }
    }

    /// <inheritdoc />
    public GitBranchCreateResult CreateBranch(string workspacePath, string branchName)
    {
        if (string.IsNullOrWhiteSpace(branchName))
        {
            return new GitBranchCreateResult(false, FAILURE_CODE_INVALID_REQUEST, "Branch name must not be empty.", null);
        }

        try
        {
            using Repository repository = OpenRepository(workspacePath);
            if (repository.Branches[branchName] is not null)
            {
                return new GitBranchCreateResult(false, FAILURE_CODE_BRANCH_ALREADY_EXISTS, $"Branch '{branchName}' already exists.", branchName);
            }

            Branch created = repository.CreateBranch(branchName);
            return new GitBranchCreateResult(true, null, null, created.FriendlyName);
        }
        catch (RepositoryNotFoundException)
        {
            return new GitBranchCreateResult(false, FAILURE_CODE_NOT_GIT_REPOSITORY, NOT_GIT_REPOSITORY_MESSAGE, null);
        }
        catch (LibGit2SharpException ex)
        {
            return new GitBranchCreateResult(false, FAILURE_CODE_BRANCH_CREATE_FAILED, ex.Message, null);
        }
    }

    /// <inheritdoc />
    public GitCommitResult StageAndCommit(string workspacePath, IReadOnlyList<string> relativePaths, string message)
    {
        if (relativePaths is null || relativePaths.Count == 0)
        {
            return new GitCommitResult(false, FAILURE_CODE_NO_CHANGES, "No files provided to stage.", null);
        }

        try
        {
            using Repository repository = OpenRepository(workspacePath);

            foreach (string relativePath in relativePaths)
            {
                string normalized = NormalizeRepositoryPath(relativePath);
                Commands.Stage(repository, normalized);
            }

            RepositoryStatus status = repository.RetrieveStatus(new StatusOptions { IncludeUnaltered = false });
            if (!status.Any(entry => IsStaged(entry.State)))
            {
                return new GitCommitResult(false, FAILURE_CODE_NO_CHANGES, "No changes were staged after adding the specified files.", null);
            }

            Signature signature = BuildStashSignature(repository);
            Commit commit = repository.Commit(message, signature, signature);
            return new GitCommitResult(true, null, null, commit.Sha);
        }
        catch (RepositoryNotFoundException)
        {
            return new GitCommitResult(false, FAILURE_CODE_NOT_GIT_REPOSITORY, NOT_GIT_REPOSITORY_MESSAGE, null);
        }
        catch (LibGit2SharpException ex)
        {
            return new GitCommitResult(false, FAILURE_CODE_COMMIT_FAILED, ex.Message, null);
        }
    }

    /// <inheritdoc />
    public GitMergeResult MergeBranch(string workspacePath, string sourceBranch)
    {
        if (string.IsNullOrWhiteSpace(sourceBranch))
        {
            return new GitMergeResult(false, FAILURE_CODE_INVALID_REQUEST, "Source branch name must not be empty.", null);
        }

        try
        {
            using Repository repository = OpenRepository(workspacePath);
            Branch? branch = repository.Branches[sourceBranch];
            if (branch is null)
            {
                return new GitMergeResult(false, FAILURE_CODE_BRANCH_NOT_FOUND, $"Branch '{sourceBranch}' was not found.", null);
            }

            Signature signature = BuildStashSignature(repository);
            MergeResult result = repository.Merge(branch, signature, new MergeOptions
            {
                FailOnConflict = true
            });

            if (result.Status == MergeStatus.Conflicts)
            {
                List<string> conflicting = repository.Index.Conflicts
                    .Select(conflict => conflict.Ancestor?.Path ?? conflict.Ours?.Path ?? conflict.Theirs?.Path ?? "unknown")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return new GitMergeResult(false, FAILURE_CODE_MERGE_CONFLICT, "Merge produced conflicts.", conflicting);
            }

            return new GitMergeResult(true, null, null, null);
        }
        catch (RepositoryNotFoundException)
        {
            return new GitMergeResult(false, FAILURE_CODE_NOT_GIT_REPOSITORY, NOT_GIT_REPOSITORY_MESSAGE, null);
        }
        catch (CheckoutConflictException ex)
        {
            return new GitMergeResult(false, FAILURE_CODE_MERGE_CONFLICT, ex.Message, null);
        }
        catch (LibGit2SharpException ex)
        {
            return new GitMergeResult(false, FAILURE_CODE_MERGE_FAILED, ex.Message, null);
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

    private static Branch? ResolveLocalBranch(Repository repository, string branchName)
    {
        Branch? localBranch = repository.Branches[branchName];
        if (localBranch is not null && !localBranch.IsRemote)
        {
            return localBranch;
        }

        Branch? remoteBranch = FindRemoteBranch(repository, branchName);
        if (remoteBranch is null)
        {
            return null;
        }

        Branch? trackedBranch = repository.Branches[branchName];
        if (trackedBranch is null || trackedBranch.IsRemote)
        {
            trackedBranch = repository.CreateBranch(branchName, remoteBranch.Tip);
        }

        string remoteName = GetRemoteName(remoteBranch);
        repository.Branches.Update(trackedBranch, updater =>
        {
            updater.Remote = remoteName;
            updater.TrackedBranch = remoteBranch.CanonicalName;
        });

        return trackedBranch;
    }

    private static Branch? FindRemoteBranch(Repository repository, string branchName)
    {
        string originFriendlyName = $"origin/{branchName}";
        Branch? originBranch = repository.Branches.FirstOrDefault(branch =>
            branch.IsRemote
            && string.Equals(branch.FriendlyName, originFriendlyName, StringComparison.OrdinalIgnoreCase));
        if (originBranch is not null)
        {
            return originBranch;
        }

        return repository.Branches.FirstOrDefault(branch =>
            branch.IsRemote
            && string.Equals(NormalizeRemoteBranchName(branch.FriendlyName), branchName, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeRemoteBranchName(string? branchName)
    {
        string normalized = NormalizeRepositoryPath(branchName);
        int separatorIndex = normalized.IndexOf('/');
        return separatorIndex >= 0 ? normalized[(separatorIndex + 1)..] : normalized;
    }

    private static string GetRemoteName(Branch remoteBranch)
    {
        string friendlyName = NormalizeRepositoryPath(remoteBranch.FriendlyName);
        int separatorIndex = friendlyName.IndexOf('/');
        return separatorIndex > 0 ? friendlyName[..separatorIndex] : "origin";
    }

    private static void FetchAllRemotes(Repository repository, GitAuthenticationOptions authentication)
    {
        FetchOptions fetchOptions = new FetchOptions();
        LibGit2Sharp.Handlers.CredentialsHandler? credentialsProvider = BuildCredentialsProvider(authentication);
        if (credentialsProvider is not null)
        {
            fetchOptions.CredentialsProvider = credentialsProvider;
        }

        foreach (Remote remote in repository.Network.Remotes)
        {
            Commands.Fetch(repository, remote.Name, Array.Empty<string>(), fetchOptions, null);
        }
    }

    private static LibGit2Sharp.Handlers.CredentialsHandler? BuildCredentialsProvider(GitAuthenticationOptions? authentication)
    {
        if (authentication is null || string.IsNullOrWhiteSpace(authentication.Password))
        {
            return null;
        }

        string username = string.IsNullOrWhiteSpace(authentication.Username) ? "git" : authentication.Username.Trim();
        string password = authentication.Password.Trim();

        return (_url, _user, _types) => new UsernamePasswordCredentials
        {
            Username = username,
            Password = password
        };
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

    private static string? TryBuildAddedFileDiff(Repository repository, string normalizedPath, FileStatus? status)
    {
        if (status is null || !IsAddedStatus(status.Value))
        {
            return null;
        }

        string workingDirectory = repository.Info.WorkingDirectory;
        string fullPath = Path.Combine(workingDirectory, normalizedPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            return null;
        }

        string fileText;
        try
        {
            fileText = File.ReadAllText(fullPath);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (DecoderFallbackException)
        {
            return null;
        }

        string normalizedText = fileText.Replace("\r\n", "\n").Replace('\r', '\n');
        string[] lines = normalizedText.Split('\n');
        bool hasTrailingNewline = normalizedText.EndsWith("\n", StringComparison.Ordinal);
        int lineCount = hasTrailingNewline ? lines.Length - 1 : lines.Length;

        List<string> diffLines = new List<string>
        {
            $"diff --git a/{normalizedPath} b/{normalizedPath}",
            "new file mode 100644",
            "--- /dev/null",
            $"+++ b/{normalizedPath}",
            $"@@ -0,0 +1,{Math.Max(lineCount, 0)} @@"
        };

        for (int index = 0; index < lineCount; index += 1)
        {
            diffLines.Add($"+{lines[index]}");
        }

        if (!hasTrailingNewline && lineCount > 0)
        {
            diffLines.Add("\\ No newline at end of file");
        }

        return string.Join("\n", diffLines);
    }

    private static bool IsAddedStatus(FileStatus state)
    {
        return state.HasFlag(FileStatus.NewInIndex)
            || state.HasFlag(FileStatus.NewInWorkdir);
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

    private static Signature BuildStashSignature(Repository repository)
    {
        string? configuredName = repository.Config.Get<string>("user.name")?.Value;
        string? configuredEmail = repository.Config.Get<string>("user.email")?.Value;

        string name = string.IsNullOrWhiteSpace(configuredName)
            ? Environment.UserName
            : configuredName.Trim();
        string email = string.IsNullOrWhiteSpace(configuredEmail)
            ? $"{NormalizeEmailLocalPart(name)}@local.archharness"
            : configuredEmail.Trim();

        return new Signature(name, email, DateTimeOffset.Now);
    }

    private static string NormalizeEmailLocalPart(string value)
    {
        string normalized = string.Concat(value
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-'))
            .Trim('-');

        return string.IsNullOrWhiteSpace(normalized) ? "archharness" : normalized;
    }
}
