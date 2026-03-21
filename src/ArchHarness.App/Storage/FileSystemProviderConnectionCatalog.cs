using System.Security.Cryptography;
using System.Text.Json;
using ArchHarness.App.Core;
using ArchHarness.App.SourceControl;

namespace ArchHarness.App.Storage;

/// <summary>
/// Persists provider connections in a user-scoped JSON file.
/// </summary>
public sealed class FileSystemProviderConnectionCatalog : IProviderConnectionCatalog
{
    private readonly SemaphoreSlim _sync = new SemaphoreSlim(1, 1);
    private readonly string _storageFilePath;
    private readonly IPersonalAccessTokenProtector _personalAccessTokenProtector;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemProviderConnectionCatalog"/> class.
    /// </summary>
    public FileSystemProviderConnectionCatalog(IPersonalAccessTokenProtector personalAccessTokenProtector)
        : this(GetDefaultStorageFilePath(), personalAccessTokenProtector)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemProviderConnectionCatalog"/> class using an explicit path.
    /// </summary>
    public FileSystemProviderConnectionCatalog(string storageFilePath, IPersonalAccessTokenProtector personalAccessTokenProtector)
    {
        this._storageFilePath = FileSystemStorageHelper.NormalizePath(storageFilePath);
        this._personalAccessTokenProtector = personalAccessTokenProtector;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProviderConnectionSettings>> GetProvidersAsync()
    {
        await this._sync.WaitAsync().ConfigureAwait(false);
        try
        {
            return await this.LoadProvidersAsync().ConfigureAwait(false);
        }
        finally
        {
            this._sync.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveProviderAsync(ProviderConnectionSettings settings)
    {
        await this._sync.WaitAsync().ConfigureAwait(false);
        try
        {
            List<PersistedProviderConnection> persistedProviders = this.LoadPersistedProviders().ToList();
            int existingIndex = persistedProviders.FindIndex(provider =>
                string.Equals(provider.DisplayName, settings.DisplayName, StringComparison.OrdinalIgnoreCase));
            PersistedProviderConnection? existing = existingIndex >= 0 ? persistedProviders[existingIndex] : null;
            PersistedProviderConnection persisted = await this.MapToPersistedAsync(settings, existing).ConfigureAwait(false);

            if (existingIndex >= 0)
            {
                persistedProviders[existingIndex] = persisted;
            }
            else
            {
                persistedProviders.Add(persisted);
            }

            await this.SavePersistedProvidersAsync(persistedProviders).ConfigureAwait(false);
        }
        finally
        {
            this._sync.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteProviderAsync(string displayName)
    {
        await this._sync.WaitAsync().ConfigureAwait(false);
        try
        {
            List<PersistedProviderConnection> providers = this.LoadPersistedProviders().ToList();
            int removedCount = providers.RemoveAll(provider =>
                string.Equals(provider.DisplayName, displayName, StringComparison.OrdinalIgnoreCase));

            if (removedCount == 0)
            {
                return false;
            }

            await this.SavePersistedProvidersAsync(providers).ConfigureAwait(false);
            return true;
        }
        finally
        {
            this._sync.Release();
        }
    }

    private async Task<IReadOnlyList<ProviderConnectionSettings>> LoadProvidersAsync()
    {
        List<PersistedProviderConnection> persistedProviders = this.LoadPersistedProviders().ToList();
        await this.MigrateLegacyPlainTextTokensAsync(persistedProviders).ConfigureAwait(false);

        List<ProviderConnectionSettings> providers = new List<ProviderConnectionSettings>(persistedProviders.Count);
        foreach (PersistedProviderConnection persistedProvider in persistedProviders)
        {
            providers.Add(await this.MapFromPersistedAsync(persistedProvider).ConfigureAwait(false));
        }

        return providers.ToArray();
    }

    private IReadOnlyList<PersistedProviderConnection> LoadPersistedProviders()
    {
        if (!File.Exists(this._storageFilePath))
        {
            return Array.Empty<PersistedProviderConnection>();
        }

        string json;
        try
        {
            json = File.ReadAllText(this._storageFilePath);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException($"Unable to read provider connections from '{this._storageFilePath}'.", ex);
        }

        List<PersistedProviderConnection>? persistedProviders;
        try
        {
            persistedProviders = JsonSerializer.Deserialize<List<PersistedProviderConnection>>(json, JsonDefaults.WEB_INDENTED);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Stored provider connections are not valid JSON.", ex);
        }

        if (persistedProviders is null || persistedProviders.Count == 0)
        {
            return Array.Empty<PersistedProviderConnection>();
        }

        return persistedProviders;
    }

    private Task SavePersistedProvidersAsync(IReadOnlyList<PersistedProviderConnection> providers)
    {
        PersistedProviderConnection[] persistedProviders = providers
            .OrderBy(provider => provider.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return FileSystemStorageHelper.WriteJsonFileAsync(this._storageFilePath, persistedProviders, JsonDefaults.WEB_INDENTED, CancellationToken.None);
    }

    private async Task<ProviderConnectionSettings> MapFromPersistedAsync(PersistedProviderConnection persisted)
    {
        string? personalAccessToken = null;
        PersonalAccessTokenStorageMode storageMode = persisted.PersonalAccessTokenStorageMode;

        if (!string.IsNullOrWhiteSpace(persisted.EncryptedPersonalAccessToken))
        {
            try
            {
                personalAccessToken = await this._personalAccessTokenProtector.UnprotectAsync(persisted.EncryptedPersonalAccessToken).ConfigureAwait(false);
                storageMode = PersonalAccessTokenStorageMode.Protected;
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException or PlatformNotSupportedException)
            {
                string displayName = string.IsNullOrWhiteSpace(persisted.DisplayName) ? "unknown" : persisted.DisplayName;
                throw new InvalidOperationException($"Stored credentials for provider '{displayName}' could not be decrypted.", ex);
            }
        }
        else if (!string.IsNullOrWhiteSpace(persisted.PlainTextPersonalAccessToken))
        {
            // Plain-text tokens remain loadable because users can explicitly choose this fallback after a warning.
            personalAccessToken = persisted.PlainTextPersonalAccessToken;
            storageMode = PersonalAccessTokenStorageMode.PlainText;
        }

        return new ProviderConnectionSettings
        {
            Provider = persisted.Provider,
            DisplayName = persisted.DisplayName,
            ServerUrl = persisted.ServerUrl,
            Organization = persisted.Organization,
            GitHubOwnerType = persisted.GitHubOwnerType,
            GitHubAuthenticationMode = persisted.GitHubAuthenticationMode,
            GitHubAuthenticatedUser = persisted.GitHubAuthenticatedUser,
            PersonalAccessToken = personalAccessToken,
            HasStoredPersonalAccessToken = !string.IsNullOrWhiteSpace(persisted.EncryptedPersonalAccessToken)
                || !string.IsNullOrWhiteSpace(persisted.PlainTextPersonalAccessToken),
            PersonalAccessTokenStorageMode = storageMode,
            IsEnabled = persisted.IsEnabled
        };
    }

    private async Task<PersistedProviderConnection> MapToPersistedAsync(ProviderConnectionSettings settings, PersistedProviderConnection? existing)
    {
        string? encryptedPersonalAccessToken = null;

        if (settings.ClearPersonalAccessToken)
        {
            return new PersistedProviderConnection(
                settings.Provider,
                settings.DisplayName,
                settings.ServerUrl,
                settings.Organization,
                settings.GitHubOwnerType,
                settings.GitHubAuthenticationMode,
                settings.GitHubAuthenticatedUser,
                null,
                null,
                existing?.PersonalAccessTokenStorageMode ?? settings.PersonalAccessTokenStorageMode,
                settings.IsEnabled);
        }

        if (!string.IsNullOrWhiteSpace(settings.PersonalAccessToken))
        {
            if (settings.PersonalAccessTokenStorageMode == PersonalAccessTokenStorageMode.PlainText)
            {
                // This fallback is intentional: the user has already been warned and chose plain-text storage.
                return new PersistedProviderConnection(
                    settings.Provider,
                    settings.DisplayName,
                    settings.ServerUrl,
                    settings.Organization,
                    settings.GitHubOwnerType,
                    settings.GitHubAuthenticationMode,
                    settings.GitHubAuthenticatedUser,
                    null,
                    settings.PersonalAccessToken,
                    PersonalAccessTokenStorageMode.PlainText,
                    settings.IsEnabled);
            }

            if (!this._personalAccessTokenProtector.CanProtect)
            {
                throw new InvalidOperationException(
                    this._personalAccessTokenProtector.UnavailableReason
                        ?? "Secure personal access token storage is required on this platform.");
            }

            encryptedPersonalAccessToken = await this._personalAccessTokenProtector.ProtectAsync(settings.PersonalAccessToken, existing?.EncryptedPersonalAccessToken).ConfigureAwait(false);
        }
        else if (ShouldPreserveExistingPersonalAccessToken(settings, existing))
        {
            return new PersistedProviderConnection(
                settings.Provider,
                settings.DisplayName,
                settings.ServerUrl,
                settings.Organization,
                settings.GitHubOwnerType,
                settings.GitHubAuthenticationMode,
                settings.GitHubAuthenticatedUser,
                existing!.EncryptedPersonalAccessToken,
                existing.PlainTextPersonalAccessToken,
                existing.PersonalAccessTokenStorageMode,
                settings.IsEnabled);
        }

        return new PersistedProviderConnection(
            settings.Provider,
            settings.DisplayName,
            settings.ServerUrl,
            settings.Organization,
            settings.GitHubOwnerType,
            settings.GitHubAuthenticationMode,
            settings.GitHubAuthenticatedUser,
            encryptedPersonalAccessToken,
            null,
            settings.PersonalAccessTokenStorageMode,
            settings.IsEnabled);
    }

    private static bool ShouldPreserveExistingPersonalAccessToken(ProviderConnectionSettings settings, PersistedProviderConnection? existing)
    {
        if (settings.ClearPersonalAccessToken)
        {
            return false;
        }

        if (existing is null)
        {
            return false;
        }

        if (settings.RetainPersonalAccessToken)
        {
            return !string.IsNullOrWhiteSpace(existing.EncryptedPersonalAccessToken)
                || !string.IsNullOrWhiteSpace(existing.PlainTextPersonalAccessToken);
        }

        return !string.IsNullOrWhiteSpace(existing.EncryptedPersonalAccessToken);
    }

    private async Task MigrateLegacyPlainTextTokensAsync(List<PersistedProviderConnection> persistedProviders)
    {
        bool updated = false;

        for (int index = 0; index < persistedProviders.Count; index++)
        {
            PersistedProviderConnection persisted = persistedProviders[index];
            if (string.IsNullOrWhiteSpace(persisted.PlainTextPersonalAccessToken))
            {
                continue;
            }

            if (!this._personalAccessTokenProtector.CanProtect)
            {
                // Plain-text storage is still a supported fallback, so lack of secure storage must not block loading it.
                continue;
            }

            // When secure storage becomes available later, opportunistically upgrade the token without changing behavior.
            persistedProviders[index] = persisted with
            {
                EncryptedPersonalAccessToken = await this._personalAccessTokenProtector.ProtectAsync(
                    persisted.PlainTextPersonalAccessToken,
                    persisted.EncryptedPersonalAccessToken).ConfigureAwait(false),
                PlainTextPersonalAccessToken = null,
                PersonalAccessTokenStorageMode = PersonalAccessTokenStorageMode.Protected
            };
            updated = true;
        }

        if (updated)
        {
            await this.SavePersistedProvidersAsync(persistedProviders).ConfigureAwait(false);
        }
    }

    private static string GetDefaultStorageFilePath()
        => FileSystemStorageHelper.GetAppDataFilePath("providers.json");

    // This persisted shape intentionally keeps a plain-text field because the product supports an explicit
    // user-approved fallback when no secure store is available. Do not remove it unless the fallback itself changes.
    private sealed record PersistedProviderConnection(
        SourceControlProvider Provider,
        string? DisplayName,
        string? ServerUrl,
        string? Organization,
        GitHubOwnerType GitHubOwnerType,
        GitHubAuthenticationMode GitHubAuthenticationMode,
        string? GitHubAuthenticatedUser,
        string? EncryptedPersonalAccessToken,
        string? PlainTextPersonalAccessToken,
        PersonalAccessTokenStorageMode PersonalAccessTokenStorageMode,
        bool IsEnabled);
}
