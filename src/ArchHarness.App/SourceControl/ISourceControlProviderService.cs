namespace ArchHarness.App.SourceControl;

/// <summary>
/// Manages configured source control provider connections.
/// </summary>
public interface ISourceControlProviderService
{
    /// <summary>
    /// Tests whether the supplied provider settings can authenticate successfully.
    /// </summary>
    Task<ConnectionTestResult> TestConnectionAsync(ProviderConnectionSettings settings);

    /// <summary>
    /// Gets all configured provider connections.
    /// </summary>
    Task<IReadOnlyList<ProviderConnectionSettings>> GetConfiguredProvidersAsync();

    /// <summary>
    /// Saves a provider connection, adding a new entry or updating an existing one.
    /// </summary>
    Task SaveProviderAsync(ProviderConnectionSettings settings);

    /// <summary>
    /// Deletes a provider connection by display name.
    /// </summary>
    Task DeleteProviderAsync(string displayName);
}
