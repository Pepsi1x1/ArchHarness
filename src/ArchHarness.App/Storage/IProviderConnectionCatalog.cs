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
    IReadOnlyList<ProviderConnectionSettings> GetProviders();

    /// <summary>
    /// Saves a provider connection.
    /// </summary>
    void SaveProvider(ProviderConnectionSettings settings);

    /// <summary>
    /// Deletes a provider connection.
    /// </summary>
    bool DeleteProvider(string displayName);
}
