using System.Text.RegularExpressions;

namespace ArchHarness.App.Core;

/// <summary>
/// Provides workspace-aware analysis utilities for language detection, objective path enforcement,
/// and classification of step objectives as review-type work.
/// </summary>
public sealed class WorkspaceContextAnalyzer : IWorkspaceContextAnalyzer
{
    /// <summary>
    /// Detects which programming languages are present in the workspace by scanning for
    /// language-specific project files and source files.
    /// </summary>
    /// <param name="workspaceRoot">The root path of the workspace to scan.</param>
    /// <returns>A list of detected language identifiers (e.g. "dotnet", "vue3").</returns>
    public IReadOnlyList<string> DetectWorkspaceLanguages(string workspaceRoot)
    {
        List<string> output = new List<string>();

        bool hasDotnet = Directory.GetFiles(workspaceRoot, "*.csproj", SearchOption.AllDirectories).Length > 0
            || Directory.GetFiles(workspaceRoot, "*.cs", SearchOption.AllDirectories).Length > 0;
        if (hasDotnet)
        {
            output.Add("dotnet");
        }

        bool hasVue = Directory.GetFiles(workspaceRoot, "*.vue", SearchOption.AllDirectories).Length > 0
            || File.Exists(Path.Combine(workspaceRoot, "package.json"));
        if (hasVue)
        {
            output.Add("vue3");
        }

        if (output.Count == 0)
        {
            output.Add("dotnet");
        }

        return output;
    }

    /// <summary>
    /// Rewrites any absolute file-system paths in an objective string that fall outside the
    /// workspace root so they point back into the workspace.
    /// </summary>
    /// <param name="objective">The objective text to sanitize.</param>
    /// <param name="workspaceRoot">The workspace root to enforce.</param>
    /// <returns>The objective with out-of-workspace paths replaced.</returns>
    public string EnforceWorkspaceRootInObjective(string objective, string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(objective))
        {
            return objective;
        }

        string normalizedRoot = Path.GetFullPath(workspaceRoot).TrimEnd('\\', '/');
        const string WINDOWS_PATH_PATTERN = "(?<![A-Za-z0-9_])([A-Za-z]:\\\\[^\\s\\\"']+)";

        return Regex.Replace(objective, WINDOWS_PATH_PATTERN, match =>
        {
            string originalPath = match.Groups[1].Value;
            try
            {
                string full = Path.GetFullPath(originalPath).TrimEnd('\\', '/');
                if (full.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return originalPath;
                }

                return normalizedRoot;
            }
            catch
            {
                return normalizedRoot;
            }
        });
    }

    /// <summary>
    /// Determines whether a step objective describes review/enforcement work rather than
    /// design or specification work.
    /// </summary>
    /// <param name="objective">The objective text to classify.</param>
    /// <returns><c>true</c> if the objective looks like review/enforcement; otherwise <c>false</c>.</returns>
    public bool IsReviewObjective(string objective)
    {
        if (string.IsNullOrWhiteSpace(objective))
        {
            return false;
        }

        string text = objective.ToLowerInvariant();
        bool looksLikeDesign = text.Contains("design")
            || text.Contains("spec")
            || text.Contains("concept")
            || text.Contains("define")
            || text.Contains("namespace layout")
            || text.Contains("project structure");

        if (looksLikeDesign)
        {
            return false;
        }

        bool looksLikeReview = text.Contains("review")
            || text.Contains("verify")
            || text.Contains("enforce")
            || text.Contains("validate")
            || text.Contains("audit");

        return looksLikeReview;
    }
}
