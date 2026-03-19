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
        this._storageFilePath = storageFilePath;
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
        => this.LoadPersistedProviders().Select(this.MapFromPersisted).ToArray();

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
        string? directory = Path.GetDirectoryName(this._storageFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        PersistedProviderConnection[] persistedProviders = providers
            .OrderBy(provider => provider.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        string json = JsonSerializer.Serialize(persistedProviders, JsonDefaults.WEB_INDENTED);
        File.WriteAllText(this._storageFilePath, json);
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
            personalAccessToken = persisted.PlainTextPersonalAccessToken;
            storageMode = PersonalAccessTokenStorageMode.PlainText;
        }

        return new ProviderConnectionSettings
        {
            Provider = persisted.Provider,
            DisplayName = persisted.DisplayName,
            ServerUrl = persisted.ServerUrl,
            Organization = persisted.Organization,
            PersonalAccessToken = personalAccessToken,
            PersonalAccessTokenStorageMode = storageMode,
            IsEnabled = persisted.IsEnabled
        };
    }

    private PersistedProviderConnection MapToPersisted(ProviderConnectionSettings settings, string? existingProtectedPersonalAccessToken)
    {
        string? encryptedPersonalAccessToken = null;
        string? plainTextPersonalAccessToken = null;

        if (!string.IsNullOrWhiteSpace(settings.PersonalAccessToken))
        {
            if (settings.PersonalAccessTokenStorageMode == PersonalAccessTokenStorageMode.PlainText)
            {
                plainTextPersonalAccessToken = settings.PersonalAccessToken;
            }
            else
            {
                if (!this._personalAccessTokenProtector.CanProtect)
                {
                    throw new PlainTextPersonalAccessTokenConfirmationRequiredException(
                        this._personalAccessTokenProtector.UnavailableReason
                            ?? "Secure personal access token storage is unavailable on this platform.");
                }

                encryptedPersonalAccessToken = this._personalAccessTokenProtector.Protect(settings.PersonalAccessToken, existingProtectedPersonalAccessToken);
            }
        }

        return new PersistedProviderConnection(
            settings.Provider,
            settings.DisplayName,
            settings.ServerUrl,
            settings.Organization,
            encryptedPersonalAccessToken,
            plainTextPersonalAccessToken,
            settings.PersonalAccessTokenStorageMode,
            settings.IsEnabled);
    }

    private static string GetDefaultStorageFilePath()
    {
        string appDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appDataRoot, "ArchHarness", "providers.json");
    }

    private sealed record PersistedProviderConnection(
        SourceControlProvider Provider,
        string? DisplayName,
        string? ServerUrl,
        string? Organization,
        string? EncryptedPersonalAccessToken,
        string? PlainTextPersonalAccessToken,
        PersonalAccessTokenStorageMode PersonalAccessTokenStorageMode,
        bool IsEnabled);
}
