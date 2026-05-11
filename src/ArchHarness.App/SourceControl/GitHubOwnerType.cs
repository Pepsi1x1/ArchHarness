namespace ArchHarness.App.SourceControl;

/// <summary>
/// Indicates how a GitHub owner should be resolved.
/// </summary>
public enum GitHubOwnerType
{
    /// <summary>
    /// Uses GitHub organization endpoints.
    /// </summary>
    Organization = 0,

    /// <summary>
    /// Uses GitHub user endpoints.
    /// </summary>
    User = 1,
}
