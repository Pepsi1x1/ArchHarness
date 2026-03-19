namespace ArchHarness.Web.Services;

/// <summary>
/// Represents a request to clone the configured project repository into the workspace folder.
/// </summary>
public sealed record CloneProjectRepositoryRequest(string? BranchName);