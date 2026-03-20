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
    public async Task<ConnectionTestResult> TestConnectionAsync(ProviderConnectionSettings settings)
    {
        ProviderConnectionSettings normalized = await this._settingsCoordinator.PrepareForConnectionTestAsync(settings).ConfigureAwait(false);
        this._settingsCoordinator.ValidateOrThrow(normalized, requirePersonalAccessToken: RequiresPersonalAccessTokenForConnectionTest(normalized.Provider));

        ISourceControlReviewProviderService service = this._providerFactory.GetProvider(normalized.Provider);
        return await service.TestConnectionAsync(normalized, CancellationToken.None).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProviderConnectionSettings>> GetConfiguredProvidersAsync()
    {
        ProviderConnectionSettings[] providers = (await this._providerConnectionCatalog
            .GetProvidersAsync()
            .ConfigureAwait(false))
            .OrderBy(provider => provider.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(provider => provider.WithoutPersonalAccessToken())
            .ToArray();

        return providers;
    }

    /// <inheritdoc />
    public async Task SaveProviderAsync(ProviderConnectionSettings settings)
    {
        ProviderConnectionSettings normalized = await this._settingsCoordinator.PrepareForSaveAsync(settings).ConfigureAwait(false);
        this._settingsCoordinator.ValidateOrThrow(normalized, requirePersonalAccessToken: false);

        if (string.IsNullOrWhiteSpace(normalized.PersonalAccessToken) && RequiresPersonalAccessTokenForSave(normalized.Provider))
        {
            throw new InvalidOperationException("PersonalAccessToken is required when creating a provider connection.");
        }

        await this._providerConnectionCatalog.SaveProviderAsync(normalized).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteProviderAsync(string displayName)
    {
        string? normalizedDisplayName = NormalizeText(displayName);
        if (string.IsNullOrWhiteSpace(normalizedDisplayName))
        {
            throw new InvalidOperationException("DisplayName is required.");
        }

        if (!await this._providerConnectionCatalog.DeleteProviderAsync(normalizedDisplayName).ConfigureAwait(false))
        {
            throw new KeyNotFoundException($"Provider '{normalizedDisplayName}' was not found.");
        }
    }

    private static bool RequiresPersonalAccessTokenForSave(SourceControlProvider provider)
        => provider is not SourceControlProvider.GitHub;

    private static bool RequiresPersonalAccessTokenForConnectionTest(SourceControlProvider provider)
        => provider is not SourceControlProvider.GitHub;

    private static string? NormalizeText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
