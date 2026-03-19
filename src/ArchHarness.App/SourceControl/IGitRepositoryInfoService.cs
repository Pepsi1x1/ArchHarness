namespace ArchHarness.App.SourceControl;

/// <summary>
/// Reads lightweight Git repository metadata for workspace paths.
/// </summary>
public interface IGitRepositoryInfoService
{
    /// <summary>
    /// Returns current branch information for a workspace path.
    /// </summary>
    GitRepositoryBranchInfo GetBranchInfo(string workspacePath);

    /// <summary>
    /// Switches the repository to the requested local branch when safe to do so.
    /// </summary>
    GitBranchCheckoutResult CheckoutBranch(string workspacePath, string branchName);

    /// <summary>
    /// Returns the current working tree changes for a workspace path.
    /// </summary>
    GitWorkingTreeStatus GetWorkingTreeStatus(string workspacePath);

    /// <summary>
    /// Returns the Git diff for a changed file in the working tree.
    /// </summary>
    GitWorkingTreeDiffResult GetWorkingTreeDiff(string workspacePath, string relativePath);

    /// <summary>
    /// Creates a stash entry for the current working tree.
    /// </summary>
    GitStashChangesResult StashWorkingTreeChanges(string workspacePath, string? message);
}

/// <summary>
/// Describes branch information for a workspace repository.
/// </summary>
/// <param name="IsGitRepository">Whether the workspace path is inside a Git repository.</param>
/// <param name="CurrentBranch">The currently checked-out branch, when available.</param>
/// <param name="Branches">Local branch names visible in the repository.</param>
public sealed record GitRepositoryBranchInfo(bool IsGitRepository, string? CurrentBranch, IReadOnlyList<string> Branches);

/// <summary>
/// Describes the outcome of a branch checkout request.
/// </summary>
/// <param name="Succeeded">Whether the checkout succeeded.</param>
/// <param name="FailureCode">Machine-readable failure code when checkout fails.</param>
/// <param name="ErrorMessage">Human-readable error when checkout fails.</param>
/// <param name="BranchInfo">Current branch information after the attempted checkout.</param>
public sealed record GitBranchCheckoutResult(bool Succeeded, string? FailureCode, string? ErrorMessage, GitRepositoryBranchInfo BranchInfo);

/// <summary>
/// Describes the changed files in a working tree.
/// </summary>
/// <param name="IsGitRepository">Whether the workspace path is inside a Git repository.</param>
/// <param name="CurrentBranch">The current branch, when available.</param>
/// <param name="HasChanges">Whether the working tree contains tracked or untracked changes.</param>
/// <param name="Files">Changed files in the working tree.</param>
public sealed record GitWorkingTreeStatus(bool IsGitRepository, string? CurrentBranch, bool HasChanges, IReadOnlyList<GitWorkingTreeFileChange> Files);

/// <summary>
/// Describes a single changed file in the working tree.
/// </summary>
/// <param name="Path">Repository-relative file path.</param>
/// <param name="Status">Human-readable Git status.</param>
/// <param name="PreviousPath">Previous path when the file was renamed, when available.</param>
/// <param name="IsStaged">Whether the file has staged changes.</param>
/// <param name="IsUntracked">Whether the file is untracked.</param>
public sealed record GitWorkingTreeFileChange(string Path, string Status, string? PreviousPath, bool IsStaged, bool IsUntracked);

/// <summary>
/// Describes the diff result for a changed file.
/// </summary>
/// <param name="IsGitRepository">Whether the workspace path is inside a Git repository.</param>
/// <param name="HasDiff">Whether a diff payload was resolved for the requested path.</param>
/// <param name="Path">Repository-relative file path that was requested.</param>
/// <param name="DiffText">Unified diff text for the file, when available.</param>
/// <param name="ErrorMessage">Human-readable error when no diff is available.</param>
public sealed record GitWorkingTreeDiffResult(bool IsGitRepository, bool HasDiff, string? Path, string? DiffText, string? ErrorMessage);

/// <summary>
/// Describes the outcome of a stash request.
/// </summary>
/// <param name="Succeeded">Whether the stash was created successfully.</param>
/// <param name="FailureCode">Machine-readable failure code when stashing fails.</param>
/// <param name="ErrorMessage">Human-readable error when stashing fails.</param>
/// <param name="BranchInfo">Current branch information after the stash attempt.</param>
/// <param name="WorkingTreeStatus">Working tree status after the stash attempt.</param>
public sealed record GitStashChangesResult(bool Succeeded, string? FailureCode, string? ErrorMessage, GitRepositoryBranchInfo BranchInfo, GitWorkingTreeStatus WorkingTreeStatus);
