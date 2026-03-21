namespace ArchHarness.Web.Services;

/// <summary>
/// Represents a request to set the source control configuration for a project.
/// </summary>
public sealed record UpdateProjectSourceControlRequest(
    string? ProviderName,
    string? ProjectName,
    string? RepositoryName);
