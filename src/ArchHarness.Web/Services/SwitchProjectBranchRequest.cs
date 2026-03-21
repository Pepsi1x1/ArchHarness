namespace ArchHarness.Web.Services;

/// <summary>
/// Requests that a project workspace switch to a local Git branch.
/// </summary>
public sealed record SwitchProjectBranchRequest(string BranchName);
