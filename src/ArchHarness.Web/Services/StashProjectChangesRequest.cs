namespace ArchHarness.Web.Services;

/// <summary>
/// Requests that a project workspace stash its local Git changes.
/// </summary>
public sealed record StashProjectChangesRequest(string? Message);
