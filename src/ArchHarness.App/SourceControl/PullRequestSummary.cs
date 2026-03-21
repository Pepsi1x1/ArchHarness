namespace ArchHarness.App.SourceControl;

/// <summary>
/// Represents a pull request summary returned by a configured source control provider.
/// </summary>
public sealed record PullRequestSummary(
    string Id,
    string Title,
    string Author,
    string SourceBranch,
    string TargetBranch,
    string Status,
    string ProjectName,
    string RepositoryName,
    string Url,
    DateTimeOffset CreatedDate);
