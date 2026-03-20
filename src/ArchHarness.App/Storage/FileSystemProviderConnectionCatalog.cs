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
    private readonly object _sync = new object();
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
    public IReadOnlyList<ProviderConnectionSettings> GetProviders()
    {
        lock (this._sync)
        {
            return this.LoadProviders();
        }
    }

    /// <inheritdoc />
    public void SaveProvider(ProviderConnectionSettings settings)
    {
        lock (this._sync)
        {
            List<PersistedProviderConnection> persistedProviders = this.LoadPersistedProviders().ToList();
            int existingIndex = persistedProviders.FindIndex(provider =>
                string.Equals(provider.DisplayName, settings.DisplayName, StringComparison.OrdinalIgnoreCase));
            PersistedProviderConnection? existing = existingIndex >= 0 ? persistedProviders[existingIndex] : null;
            PersistedProviderConnection persisted = this.MapToPersisted(settings, existing?.EncryptedPersonalAccessToken);

            if (existingIndex >= 0)
            {
                persistedProviders[existingIndex] = persisted;
            }
            else
            {
                persistedProviders.Add(persisted);
            }

            this.SavePersistedProviders(persistedProviders);
        }
    }

    /// <inheritdoc />
    public bool DeleteProvider(string displayName)
    {
        lock (this._sync)
        {
            List<PersistedProviderConnection> providers = this.LoadPersistedProviders().ToList();
            int removedCount = providers.RemoveAll(provider =>
                string.Equals(provider.DisplayName, displayName, StringComparison.OrdinalIgnoreCase));

            if (removedCount == 0)
            {
                return false;
            }

            this.SavePersistedProviders(providers);
            return true;
        }
    }

    private IReadOnlyList<ProviderConnectionSettings> LoadProviders()
    {
        List<PersistedProviderConnection> persistedProviders = this.LoadPersistedProviders().ToList();
        this.MigrateLegacyPlainTextTokens(persistedProviders);
        return persistedProviders.Select(this.MapFromPersisted).ToArray();
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

    private void SavePersistedProviders(IReadOnlyList<PersistedProviderConnection> providers)
    {
        PersistedProviderConnection[] persistedProviders = providers
            .OrderBy(provider => provider.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        FileSystemStorageHelper.WriteJsonFile(this._storageFilePath, persistedProviders, JsonDefaults.WEB_INDENTED);
    }

    private ProviderConnectionSettings MapFromPersisted(PersistedProviderConnection persisted)
    {
        string? personalAccessToken = null;
        PersonalAccessTokenStorageMode storageMode = persisted.PersonalAccessTokenStorageMode;

        if (!string.IsNullOrWhiteSpace(persisted.EncryptedPersonalAccessToken))
        {
            try
            {
                personalAccessToken = this._personalAccessTokenProtector.Unprotect(persisted.EncryptedPersonalAccessToken);
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
            PersonalAccessTokenStorageMode = storageMode,
            IsEnabled = persisted.IsEnabled
        };
    }

    private PersistedProviderConnection MapToPersisted(ProviderConnectionSettings settings, string? existingProtectedPersonalAccessToken)
    {
        string? encryptedPersonalAccessToken = null;

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

            encryptedPersonalAccessToken = this._personalAccessTokenProtector.Protect(settings.PersonalAccessToken, existingProtectedPersonalAccessToken);
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
            PersonalAccessTokenStorageMode.Protected,
            settings.IsEnabled);
    }

    private void MigrateLegacyPlainTextTokens(List<PersistedProviderConnection> persistedProviders)
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
                EncryptedPersonalAccessToken = this._personalAccessTokenProtector.Protect(
                    persisted.PlainTextPersonalAccessToken,
                    persisted.EncryptedPersonalAccessToken),
                PlainTextPersonalAccessToken = null,
                PersonalAccessTokenStorageMode = PersonalAccessTokenStorageMode.Protected
            };
            updated = true;
        }

        if (updated)
        {
            this.SavePersistedProviders(persistedProviders);
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
