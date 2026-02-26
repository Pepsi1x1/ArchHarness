namespace ArchHarness.App.Core;

/// <summary>
/// Abstracts workspace-aware analysis utilities for language detection, objective path enforcement,
/// and classification of step objectives as review-type work.
/// </summary>
public interface IWorkspaceContextAnalyzer
{
    /// <summary>
    /// Detects which programming languages are present in the workspace.
    /// </summary>
    IReadOnlyList<string> DetectWorkspaceLanguages(string workspaceRoot);

    /// <summary>
    /// Rewrites absolute file-system paths in an objective that fall outside the workspace root.
    /// </summary>
    string EnforceWorkspaceRootInObjective(string objective, string workspaceRoot);

    /// <summary>
    /// Determines whether a step objective describes review/enforcement work.
    /// </summary>
    bool IsReviewObjective(string objective);
}
