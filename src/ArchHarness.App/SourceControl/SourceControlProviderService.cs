using ArchHarness.App.Storage;

namespace ArchHarness.App.SourceControl;

/// <summary>
/// Coordinates provider connection persistence and connectivity checks.
/// </summary>
public sealed class SourceControlProviderService : ISourceControlProviderService
{
    private static readonly char[] InvalidDisplayNameCharacters = new[] { '/', '\\' };

    private readonly IProviderConnectionCatalog _providerConnectionCatalog;
    private readonly SourceControlProviderFactory _providerFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SourceControlProviderService"/> class.
    /// </summary>
    public SourceControlProviderService(
        IProviderConnectionCatalog providerConnectionCatalog,
        SourceControlProviderFactory providerFactory)
    {
        this._providerConnectionCatalog = providerConnectionCatalog;
        this._providerFactory = providerFactory;
    }

    /// <inheritdoc />
    public Task<ConnectionTestResult> TestConnectionAsync(ProviderConnectionSettings settings)
    {
        ProviderConnectionSettings normalized = Normalize(settings);
        Validate(normalized, requirePersonalAccessToken: RequiresPersonalAccessTokenForConnectionTest(normalized.Provider));

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
        ProviderConnectionSettings normalized = Normalize(settings);
        Validate(normalized, requirePersonalAccessToken: false);

        ProviderConnectionSettings? existing = this._providerConnectionCatalog
            .GetProviders()
            .FirstOrDefault(provider => string.Equals(provider.DisplayName, normalized.DisplayName, StringComparison.OrdinalIgnoreCase));

        string? personalAccessToken = normalized.PersonalAccessToken;
        PersonalAccessTokenStorageMode storageMode = normalized.PersonalAccessTokenStorageMode;
        if (string.IsNullOrWhiteSpace(personalAccessToken))
        {
            if (normalized.Provider == SourceControlProvider.GitHub)
            {
                personalAccessToken = null;
            }
            else
            {
                personalAccessToken = existing?.PersonalAccessToken;
                storageMode = existing?.PersonalAccessTokenStorageMode ?? storageMode;
            }
        }

        if (string.IsNullOrWhiteSpace(personalAccessToken) && RequiresPersonalAccessTokenForSave(normalized.Provider))
        {
            throw new InvalidOperationException("PersonalAccessToken is required when creating a provider connection.");
        }

        this._providerConnectionCatalog.SaveProvider(normalized with
        {
            PersonalAccessToken = personalAccessToken,
            PersonalAccessTokenStorageMode = storageMode
        });

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

    private static ProviderConnectionSettings Normalize(ProviderConnectionSettings settings)
        => settings with
        {
            DisplayName = NormalizeText(settings.DisplayName),
            ServerUrl = settings.Provider == SourceControlProvider.AzureDevOpsServer ? NormalizeText(settings.ServerUrl) : null,
            Organization = NormalizeText(settings.Organization),
            PersonalAccessToken = NormalizeText(settings.PersonalAccessToken)
        };

    private static void Validate(ProviderConnectionSettings settings, bool requirePersonalAccessToken)
    {
        if (!Enum.IsDefined(settings.Provider))
        {
            throw new InvalidOperationException("Provider is required.");
        }

        if (string.IsNullOrWhiteSpace(settings.DisplayName))
        {
            throw new InvalidOperationException("DisplayName is required.");
        }

        if (settings.DisplayName.IndexOfAny(InvalidDisplayNameCharacters) >= 0)
        {
            throw new InvalidOperationException("DisplayName cannot contain path separator characters.");
        }

        if (string.IsNullOrWhiteSpace(settings.Organization))
        {
            throw new InvalidOperationException("Organization is required.");
        }

        if (settings.Provider == SourceControlProvider.GitHub && !Enum.IsDefined(settings.GitHubOwnerType))
        {
            throw new InvalidOperationException("GitHubOwnerType is required for GitHub providers.");
        }

        if (settings.Provider == SourceControlProvider.AzureDevOpsServer)
        {
            if (string.IsNullOrWhiteSpace(settings.ServerUrl))
            {
                throw new InvalidOperationException("ServerUrl is required for Azure DevOps Server.");
            }

            if (!Uri.TryCreate(settings.ServerUrl, UriKind.Absolute, out _))
            {
                throw new InvalidOperationException("ServerUrl must be an absolute URL.");
            }
        }

        if (requirePersonalAccessToken && string.IsNullOrWhiteSpace(settings.PersonalAccessToken))
        {
            throw new InvalidOperationException("PersonalAccessToken is required.");
        }
    }

    private static bool RequiresPersonalAccessTokenForSave(SourceControlProvider provider)
        => provider is not SourceControlProvider.GitHub;

    private static bool RequiresPersonalAccessTokenForConnectionTest(SourceControlProvider provider)
        => provider is not SourceControlProvider.GitHub;

    private static string? NormalizeText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
