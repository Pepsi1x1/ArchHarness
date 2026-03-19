using LibGit2Sharp;

namespace ArchHarness.App.SourceControl;

/// <summary>
/// Provides Git repository metadata using LibGit2Sharp.
/// </summary>
public sealed class LibGit2SharpRepositoryInfoService : IGitRepositoryInfoService
{
    private const string FailureCodeNotGitRepository = "not-git-repository";
    private const string FailureCodeBranchNotFound = "branch-not-found";
    private const string FailureCodeCloneFailed = "clone-failed";
    private const string FailureCodeAlreadyGitRepository = "already-git-repository";
    private const string FailureCodeDirtyWorktree = "dirty-worktree";
    private const string FailureCodeCheckoutConflict = "checkout-conflict";
    private const string FailureCodeInvalidRequest = "invalid-request";
    private const string FailureCodeNoChanges = "no-changes";
    private const string FailureCodeStashFailed = "stash-failed";
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
    public GitBranchCheckoutResult CheckoutBranch(string workspacePath, string branchName, GitAuthenticationOptions? authentication = null)
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
                    FailureCodeBranchNotFound,
                    $"Branch '{normalizedBranchName}' was not found locally or on a configured remote.",
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
    public GitCloneResult CloneRepository(string workspacePath, string remoteUrl, string? branchName = null, GitAuthenticationOptions? authentication = null)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            return new GitCloneResult(
                false,
                FailureCodeInvalidRequest,
                "Workspace path is required.",
                new GitRepositoryBranchInfo(false, null, Array.Empty<string>()));
        }

        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            return new GitCloneResult(
                false,
                FailureCodeInvalidRequest,
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
                    FailureCodeAlreadyGitRepository,
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
                        FailureCodeBranchNotFound,
                        $"Branch '{branchName.Trim()}' was not found after cloning the repository.",
                        BuildBranchInfo(repository));
                }

                Commands.Checkout(repository, branch);
            }

            return new GitCloneResult(true, null, null, BuildBranchInfo(repository));
        }
        catch (LibGit2SharpException ex)
        {
            return new GitCloneResult(false, FailureCodeCloneFailed, ex.Message, GetBranchInfo(workspacePath));
        }
        catch (IOException)
        {
            return new GitCloneResult(false, FailureCodeCloneFailed, "Git could not clone the repository because the target folder is not currently accessible.", GetBranchInfo(workspacePath));
        }
        catch (UnauthorizedAccessException)
        {
            return new GitCloneResult(false, FailureCodeCloneFailed, "Git could not clone the repository because the target folder is not currently accessible.", GetBranchInfo(workspacePath));
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
                return new GitStashChangesResult(false, FailureCodeNoChanges, "There are no local changes to stash.", branchInfo, workingTreeStatus);
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
                FailureCodeNotGitRepository,
                NotGitRepositoryMessage,
                new GitRepositoryBranchInfo(false, null, Array.Empty<string>()),
                new GitWorkingTreeStatus(false, null, false, Array.Empty<GitWorkingTreeFileChange>()));
        }
        catch (LibGit2SharpException ex)
        {
            GitRepositoryBranchInfo branchInfo = GetBranchInfo(workspacePath);
            GitWorkingTreeStatus workingTreeStatus = GetWorkingTreeStatus(workspacePath);
            return new GitStashChangesResult(false, FailureCodeStashFailed, ex.Message, branchInfo, workingTreeStatus);
        }
        catch (IOException)
        {
            GitRepositoryBranchInfo branchInfo = GetBranchInfo(workspacePath);
            GitWorkingTreeStatus workingTreeStatus = GetWorkingTreeStatus(workspacePath);
            return new GitStashChangesResult(false, FailureCodeStashFailed, "Git could not create the stash because the repository files are not currently accessible.", branchInfo, workingTreeStatus);
        }
        catch (UnauthorizedAccessException)
        {
            GitRepositoryBranchInfo branchInfo = GetBranchInfo(workspacePath);
            GitWorkingTreeStatus workingTreeStatus = GetWorkingTreeStatus(workspacePath);
            return new GitStashChangesResult(false, FailureCodeStashFailed, "Git could not create the stash because the repository files are not currently accessible.", branchInfo, workingTreeStatus);
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
