using ArchHarness.App.SourceControl;

namespace ArchHarness.App.Storage;

/// <summary>
/// Persists configured provider connections.
/// </summary>
public interface IProviderConnectionCatalog
{
    /// <summary>
    /// Gets all configured provider connections.
    /// </summary>
    Task<IReadOnlyList<ProviderConnectionSettings>> GetProvidersAsync();

    /// <summary>
    /// Saves a provider connection.
    /// </summary>
    Task SaveProviderAsync(ProviderConnectionSettings settings);

    /// <summary>
    /// Deletes a provider connection.
    /// </summary>
    Task<bool> DeleteProviderAsync(string displayName);
}
