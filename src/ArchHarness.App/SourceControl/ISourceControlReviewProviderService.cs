namespace ArchHarness.App.SourceControl;

/// <summary>
/// Defines provider-specific pull request operations used by the review API.
/// </summary>
public interface ISourceControlReviewProviderService
{
    /// <summary>
    /// Verifies that the supplied provider connection settings can authenticate to the provider.
    /// </summary>
    Task<ConnectionTestResult> TestConnectionAsync(ProviderConnectionSettings settings, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves the open pull requests for the specified project and repository.
    /// </summary>
    Task<IReadOnlyList<PullRequestSummary>> GetPullRequestsAsync(
        ProviderConnectionSettings settings,
        string? projectName,
        string? repositoryName,
        CancellationToken cancellationToken,
        string? projectFilter = null,
        string? repositoryFilter = null,
        string? authorFilter = null);

    /// <summary>
    /// Streams open pull requests in batches as they are discovered by the provider.
    /// </summary>
    IAsyncEnumerable<IReadOnlyList<PullRequestSummary>> StreamPullRequestBatchesAsync(
        ProviderConnectionSettings settings,
        string? projectName,
        string? repositoryName,
        CancellationToken cancellationToken,
        string? projectFilter = null,
        string? repositoryFilter = null,
        string? authorFilter = null);

    /// <summary>
    /// Retrieves the changed files for a pull request in the specified project and repository.
    /// </summary>
    Task<IReadOnlyList<PullRequestFile>> GetPullRequestFilesAsync(
        ProviderConnectionSettings settings,
        string? projectName,
        string? repositoryName,
        string pullRequestId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resolves the HTTPS clone URL for the specified repository.
    /// </summary>
    Task<string> GetRepositoryCloneUrlAsync(
        ProviderConnectionSettings settings,
        string? projectName,
        string repositoryName,
        CancellationToken cancellationToken);
}
