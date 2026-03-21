namespace ArchHarness.App.SourceControl;

/// <summary>
/// Describes the connection settings for a source control provider.
/// </summary>
public sealed record SourceControlProviderConfig(
    SourceControlProvider ProviderType,
    string? ServerUrl,
    string? OrganizationUrl,
    string? Organization,
    string? ProjectName,
    string? RepositoryName,
    string? PersonalAccessToken,
    bool IsEnabled,
    PersonalAccessTokenStorageMode PersonalAccessTokenStorageMode = PersonalAccessTokenStorageMode.Protected)
{
    /// <summary>
    /// Creates a copy of the configuration with the access token removed.
    /// </summary>
    public SourceControlProviderConfig WithoutPersonalAccessToken()
        => this with { PersonalAccessToken = null };
}
