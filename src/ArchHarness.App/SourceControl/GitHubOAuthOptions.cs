namespace ArchHarness.App.SourceControl;

/// <summary>
/// Configuration for the GitHub OAuth device authorization flow.
/// </summary>
public sealed class GitHubOAuthOptions
{
    /// <summary>
    /// Gets or sets the GitHub OAuth app client ID.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Gets or sets the scopes requested during authorization.
    /// </summary>
    public string[] Scopes { get; set; } = new[] { "repo", "read:org" };
}