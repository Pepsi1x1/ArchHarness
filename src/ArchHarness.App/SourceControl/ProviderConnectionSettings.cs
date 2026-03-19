namespace ArchHarness.App.SourceControl;

/// <summary>
/// Describes a persisted connection to a supported source control provider.
/// </summary>
public sealed record ProviderConnectionSettings
{
    /// <summary>
    /// Gets the provider type.
    /// </summary>
    public SourceControlProvider Provider { get; init; }

    /// <summary>
    /// Gets the user-facing display name for the connection.
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Gets the Azure DevOps Server base URL when applicable.
    /// </summary>
    public string? ServerUrl { get; init; }

    /// <summary>
    /// Gets the organization, owner, or collection name used by the provider.
    /// </summary>
    public string? Organization { get; init; }

    /// <summary>
    /// Gets how a GitHub owner should be resolved when applicable.
    /// </summary>
    public GitHubOwnerType GitHubOwnerType { get; init; } = GitHubOwnerType.Organization;

    /// <summary>
    /// Gets the provider personal access token.
    /// </summary>
    public string? PersonalAccessToken { get; init; }

    /// <summary>
    /// Gets how the personal access token is stored at rest.
    /// </summary>
    public PersonalAccessTokenStorageMode PersonalAccessTokenStorageMode { get; init; } = PersonalAccessTokenStorageMode.Protected;

    /// <summary>
    /// Gets a value indicating whether the connection is enabled.
    /// </summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// Creates a copy of the settings with the access token removed.
    /// </summary>
    public ProviderConnectionSettings WithoutPersonalAccessToken()
        => this with { PersonalAccessToken = null };
}
