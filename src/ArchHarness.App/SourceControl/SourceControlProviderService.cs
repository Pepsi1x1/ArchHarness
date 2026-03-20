using ArchHarness.App.Storage;

namespace ArchHarness.App.SourceControl;

/// <summary>
/// Coordinates provider connection persistence and connectivity checks.
/// </summary>
public sealed class SourceControlProviderService : ISourceControlProviderService
{
    private readonly IProviderConnectionCatalog _providerConnectionCatalog;
    private readonly IProviderConnectionSettingsCoordinator _settingsCoordinator;
    private readonly SourceControlProviderFactory _providerFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SourceControlProviderService"/> class.
    /// </summary>
    public SourceControlProviderService(
        IProviderConnectionCatalog providerConnectionCatalog,
        IProviderConnectionSettingsCoordinator settingsCoordinator,
        SourceControlProviderFactory providerFactory)
    {
        this._providerConnectionCatalog = providerConnectionCatalog;
        this._settingsCoordinator = settingsCoordinator;
        this._providerFactory = providerFactory;
    }

    /// <inheritdoc />
    public Task<ConnectionTestResult> TestConnectionAsync(ProviderConnectionSettings settings)
    {
        ProviderConnectionSettings normalized = this._settingsCoordinator.PrepareForConnectionTest(settings);
        this._settingsCoordinator.ValidateOrThrow(normalized, requirePersonalAccessToken: RequiresPersonalAccessTokenForConnectionTest(normalized.Provider));

        ISourceControlReviewProviderService service = this._providerFactory.GetProvider(normalized.Provider);
        return service.TestConnectionAsync(normalized, CancellationToken.None);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ProviderConnectionSettings>> GetConfiguredProvidersAsync()
    {
        ProviderConnectionSettings[] providers = this._providerConnectionCatalog
            .GetProviders()
            .OrderBy(provider => provider.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(provider => provider.WithoutPersonalAccessToken())
            .ToArray();

        return Task.FromResult<IReadOnlyList<ProviderConnectionSettings>>(providers);
    }

    /// <inheritdoc />
    public Task SaveProviderAsync(ProviderConnectionSettings settings)
    {
        ProviderConnectionSettings normalized = this._settingsCoordinator.PrepareForSave(settings);
        this._settingsCoordinator.ValidateOrThrow(normalized, requirePersonalAccessToken: false);

        if (string.IsNullOrWhiteSpace(normalized.PersonalAccessToken) && RequiresPersonalAccessTokenForSave(normalized.Provider))
        {
            throw new InvalidOperationException("PersonalAccessToken is required when creating a provider connection.");
        }

        this._providerConnectionCatalog.SaveProvider(normalized);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteProviderAsync(string displayName)
    {
        string? normalizedDisplayName = NormalizeText(displayName);
        if (string.IsNullOrWhiteSpace(normalizedDisplayName))
        {
            throw new InvalidOperationException("DisplayName is required.");
        }

        if (!this._providerConnectionCatalog.DeleteProvider(normalizedDisplayName))
        {
            throw new KeyNotFoundException($"Provider '{normalizedDisplayName}' was not found.");
        }

        return Task.CompletedTask;
    }

    private static bool RequiresPersonalAccessTokenForSave(SourceControlProvider provider)
        => provider is not SourceControlProvider.GitHub;

    private static bool RequiresPersonalAccessTokenForConnectionTest(SourceControlProvider provider)
        => provider is not SourceControlProvider.GitHub;

    private static string? NormalizeText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
