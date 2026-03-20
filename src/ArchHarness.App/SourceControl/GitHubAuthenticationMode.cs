namespace ArchHarness.App.SourceControl;

/// <summary>
/// Describes how a GitHub provider authenticates requests.
/// </summary>
public enum GitHubAuthenticationMode
{
    /// <summary>
    /// No GitHub authentication is configured.
    /// </summary>
    None,

    /// <summary>
    /// A manually entered personal access token is used.
    /// </summary>
    PersonalAccessToken,

    /// <summary>
    /// An OAuth device-flow access token is used.
    /// </summary>
    OAuthDeviceFlow
}