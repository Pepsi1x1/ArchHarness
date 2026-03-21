using System.Security.Cryptography;
using System.Text;
using ArchHarness.App.SourceControl;
using ArchHarness.App.Storage;

namespace ArchHarness.App.Tests.Core;

public sealed class FileSystemProviderConnectionCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ArchHarnessProviderConnectionTests", Guid.NewGuid().ToString("N"));
    private readonly TestPersonalAccessTokenProtector _protector = new TestPersonalAccessTokenProtector(canProtect: true);

    /// <summary>
    /// Verifies that saving a provider with a PAT stores it in encrypted form and allows retrieval.
    /// </summary>
    [Fact]
    public async Task SaveProviderAsync_PersistsEncryptedPersonalAccessToken()
    {
        FileSystemProviderConnectionCatalog catalog = this.CreateCatalog();

        await catalog.SaveProviderAsync(new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.GitHub,
            DisplayName = "GitHub",
            Organization = "octo-org",
            PersonalAccessToken = "github-pat",
            IsEnabled = true
        });

        string json = await File.ReadAllTextAsync(Path.Combine(this._root, "providers.json"));
        Assert.DoesNotContain("github-pat", json);

        ProviderConnectionSettings persisted = Assert.Single(await this.CreateCatalog().GetProvidersAsync());
        Assert.Equal("github-pat", persisted.PersonalAccessToken);
        Assert.Equal(PersonalAccessTokenStorageMode.Protected, persisted.PersonalAccessTokenStorageMode);
    }

    /// <summary>
    /// Verifies that blank edits preserve an existing protected token instead of wiping it.
    /// </summary>
    [Fact]
    public async Task SaveProviderAsync_BlankPersonalAccessTokenPreservesExistingProtectedToken()
    {
        FileSystemProviderConnectionCatalog catalog = this.CreateCatalog();

        await catalog.SaveProviderAsync(new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.GitHub,
            DisplayName = "GitHub",
            Organization = "octo-org",
            PersonalAccessToken = "github-pat",
            IsEnabled = true
        });

        await catalog.SaveProviderAsync(new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.GitHub,
            DisplayName = "GitHub",
            Organization = "octo-org",
            PersonalAccessToken = null,
            IsEnabled = true
        });

        ProviderConnectionSettings persisted = Assert.Single(await this.CreateCatalog().GetProvidersAsync());
        Assert.Equal("github-pat", persisted.PersonalAccessToken);
        Assert.Equal(PersonalAccessTokenStorageMode.Protected, persisted.PersonalAccessTokenStorageMode);
    }

    /// <summary>
    /// Verifies that an explicit clear action removes an existing protected token.
    /// </summary>
    [Fact]
    public async Task SaveProviderAsync_ClearPersonalAccessTokenRemovesExistingProtectedToken()
    {
        FileSystemProviderConnectionCatalog catalog = this.CreateCatalog();

        await catalog.SaveProviderAsync(new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.GitHub,
            DisplayName = "GitHub",
            Organization = "octo-org",
            PersonalAccessToken = "github-pat",
            IsEnabled = true
        });

        await catalog.SaveProviderAsync(new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.GitHub,
            DisplayName = "GitHub",
            Organization = "octo-org",
            ClearPersonalAccessToken = true,
            IsEnabled = true
        });

        ProviderConnectionSettings persisted = Assert.Single(await this.CreateCatalog().GetProvidersAsync());
        Assert.Null(persisted.PersonalAccessToken);
        Assert.False(persisted.HasStoredPersonalAccessToken);
    }

    /// <summary>
    /// Verifies that legacy plain-text tokens are migrated to protected storage when a secure store is available.
    /// </summary>
    [Fact]
    public async Task GetProvidersAsync_MigratesLegacyPlainTextPersonalAccessTokens()
    {
        string storagePath = Path.Combine(this._root, "providers.json");
        Directory.CreateDirectory(this._root);
                await File.WriteAllTextAsync(storagePath, """
            [
              {
                "provider": 2,
                "displayName": "GitHub",
                "serverUrl": null,
                "organization": "octo-org",
                "gitHubOwnerType": 0,
                "gitHubAuthenticationMode": 0,
                "gitHubAuthenticatedUser": null,
                "encryptedPersonalAccessToken": null,
                "plainTextPersonalAccessToken": "github-pat",
                "personalAccessTokenStorageMode": 1,
                "isEnabled": true
              }
            ]
            """);

        ProviderConnectionSettings persisted = Assert.Single(await this.CreateCatalog().GetProvidersAsync());

        Assert.Equal("github-pat", persisted.PersonalAccessToken);
        Assert.Equal(PersonalAccessTokenStorageMode.Protected, persisted.PersonalAccessTokenStorageMode);

        string migratedJson = await File.ReadAllTextAsync(storagePath);
        Assert.DoesNotContain("github-pat", migratedJson);
        Assert.Contains("encryptedPersonalAccessToken", migratedJson);
    }

    /// <summary>
    /// Verifies that the GitHub owner type is persisted and restored correctly.
    /// </summary>
    [Fact]
    public async Task SaveProviderAsync_PersistsGitHubOwnerType()
    {
        FileSystemProviderConnectionCatalog catalog = this.CreateCatalog();

        await catalog.SaveProviderAsync(new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.GitHub,
            DisplayName = "GitHub User",
            Organization = "octocat",
            GitHubOwnerType = GitHubOwnerType.User,
            PersonalAccessToken = null,
            IsEnabled = true
        });

        ProviderConnectionSettings persisted = Assert.Single(await this.CreateCatalog().GetProvidersAsync());
        Assert.Equal(GitHubOwnerType.User, persisted.GitHubOwnerType);
    }

    /// <summary>
    /// Verifies that GitHub OAuth metadata (authentication mode and authenticated user) is persisted correctly.
    /// </summary>
    [Fact]
    public async Task SaveProviderAsync_PersistsGitHubOAuthMetadata()
    {
        FileSystemProviderConnectionCatalog catalog = this.CreateCatalog();

        await catalog.SaveProviderAsync(new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.GitHub,
            DisplayName = "GitHub OAuth",
            Organization = "octo-org",
            GitHubAuthenticationMode = GitHubAuthenticationMode.OAuthDeviceFlow,
            GitHubAuthenticatedUser = "octocat",
            PersonalAccessToken = "oauth-token",
            IsEnabled = true
        });

        ProviderConnectionSettings persisted = Assert.Single(await this.CreateCatalog().GetProvidersAsync());
        Assert.Equal(GitHubAuthenticationMode.OAuthDeviceFlow, persisted.GitHubAuthenticationMode);
        Assert.Equal("octocat", persisted.GitHubAuthenticatedUser);
        Assert.Equal("oauth-token", persisted.PersonalAccessToken);
    }

    /// <summary>
    /// Verifies that saving with protected storage still fails when secure storage is unavailable.
    /// </summary>
    [Fact]
    public async Task SaveProviderAsync_ThrowsWhenProtectedStorageIsUnavailable()
    {
        FileSystemProviderConnectionCatalog catalog = new FileSystemProviderConnectionCatalog(
            Path.Combine(this._root, "providers.json"),
            new TestPersonalAccessTokenProtector(canProtect: false));

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            catalog.SaveProviderAsync(new ProviderConnectionSettings
            {
                Provider = SourceControlProvider.GitHub,
                DisplayName = "GitHub",
                Organization = "octo-org",
                PersonalAccessToken = "github-pat",
                IsEnabled = true
            }));

        Assert.Contains("secure", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that explicit plain-text storage persists when secure storage is unavailable.
    /// </summary>
    [Fact]
    public async Task SaveProviderAsync_PersistsPlainTextPersonalAccessTokenWhenRequested()
    {
        FileSystemProviderConnectionCatalog catalog = new FileSystemProviderConnectionCatalog(
            Path.Combine(this._root, "providers.json"),
            new TestPersonalAccessTokenProtector(canProtect: false));

        await catalog.SaveProviderAsync(new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.GitHub,
            DisplayName = "GitHub",
            Organization = "octo-org",
            PersonalAccessToken = "github-pat",
            PersonalAccessTokenStorageMode = PersonalAccessTokenStorageMode.PlainText,
            IsEnabled = true
        });

        string json = await File.ReadAllTextAsync(Path.Combine(this._root, "providers.json"));
        Assert.Contains("plainTextPersonalAccessToken", json);
        Assert.Contains("github-pat", json);

        ProviderConnectionSettings persisted = Assert.Single(await catalog.GetProvidersAsync());
        Assert.Equal("github-pat", persisted.PersonalAccessToken);
        Assert.Equal(PersonalAccessTokenStorageMode.PlainText, persisted.PersonalAccessTokenStorageMode);
    }

    /// <summary>
    /// Verifies that retention preserves an existing plain-text token and storage mode when no new token is supplied.
    /// </summary>
    [Fact]
    public async Task SaveProviderAsync_RetainPersonalAccessTokenPreservesExistingPlainTextToken()
    {
        FileSystemProviderConnectionCatalog catalog = new FileSystemProviderConnectionCatalog(
            Path.Combine(this._root, "providers.json"),
            new TestPersonalAccessTokenProtector(canProtect: false));

        await catalog.SaveProviderAsync(new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.GitHub,
            DisplayName = "GitHub",
            Organization = "octo-org",
            PersonalAccessToken = "github-pat",
            PersonalAccessTokenStorageMode = PersonalAccessTokenStorageMode.PlainText,
            IsEnabled = true
        });

        await catalog.SaveProviderAsync(new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.GitHub,
            DisplayName = "GitHub",
            Organization = "octo-org",
            PersonalAccessToken = null,
            RetainPersonalAccessToken = true,
            IsEnabled = true
        });

        ProviderConnectionSettings persisted = Assert.Single(await catalog.GetProvidersAsync());
        Assert.Equal("github-pat", persisted.PersonalAccessToken);
        Assert.Equal(PersonalAccessTokenStorageMode.PlainText, persisted.PersonalAccessTokenStorageMode);
    }

    /// <summary>
    /// Verifies that an explicit clear action removes an existing plain-text token.
    /// </summary>
    [Fact]
    public async Task SaveProviderAsync_ClearPersonalAccessTokenRemovesExistingPlainTextToken()
    {
        FileSystemProviderConnectionCatalog catalog = new FileSystemProviderConnectionCatalog(
            Path.Combine(this._root, "providers.json"),
            new TestPersonalAccessTokenProtector(canProtect: false));

        await catalog.SaveProviderAsync(new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.GitHub,
            DisplayName = "GitHub",
            Organization = "octo-org",
            PersonalAccessToken = "github-pat",
            PersonalAccessTokenStorageMode = PersonalAccessTokenStorageMode.PlainText,
            IsEnabled = true
        });

        await catalog.SaveProviderAsync(new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.GitHub,
            DisplayName = "GitHub",
            Organization = "octo-org",
            ClearPersonalAccessToken = true,
            IsEnabled = true
        });

        ProviderConnectionSettings persisted = Assert.Single(await catalog.GetProvidersAsync());
        Assert.Null(persisted.PersonalAccessToken);
        Assert.False(persisted.HasStoredPersonalAccessToken);
    }

    /// <summary>
    /// Verifies that plain-text tokens remain readable when secure storage is unavailable.
    /// </summary>
    [Fact]
    public async Task GetProvidersAsync_LoadsPlainTextPersonalAccessTokensWhenProtectionIsUnavailable()
    {
        string storagePath = Path.Combine(this._root, "providers.json");
        Directory.CreateDirectory(this._root);
                await File.WriteAllTextAsync(storagePath, """
            [
              {
                "provider": 2,
                "displayName": "GitHub",
                "serverUrl": null,
                "organization": "octo-org",
                "gitHubOwnerType": 0,
                "gitHubAuthenticationMode": 0,
                "gitHubAuthenticatedUser": null,
                "encryptedPersonalAccessToken": null,
                "plainTextPersonalAccessToken": "github-pat",
                "personalAccessTokenStorageMode": 1,
                "isEnabled": true
              }
            ]
            """);

        FileSystemProviderConnectionCatalog catalog = new FileSystemProviderConnectionCatalog(
            storagePath,
            new TestPersonalAccessTokenProtector(canProtect: false));

        ProviderConnectionSettings persisted = Assert.Single(await catalog.GetProvidersAsync());

        Assert.Equal("github-pat", persisted.PersonalAccessToken);
        Assert.Equal(PersonalAccessTokenStorageMode.PlainText, persisted.PersonalAccessTokenStorageMode);
    }

    /// <summary>
    /// Verifies that deleting a provider by display name is case-insensitive.
    /// </summary>
    [Fact]
    public async Task DeleteProviderAsync_RemovesMatchingProviderIgnoringCase()
    {
        FileSystemProviderConnectionCatalog catalog = this.CreateCatalog();
        await catalog.SaveProviderAsync(new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.AzureDevOpsServices,
            DisplayName = "Contoso Cloud",
            Organization = "contoso",
            PersonalAccessToken = "ado-pat",
            IsEnabled = true
        });

        bool deleted = await catalog.DeleteProviderAsync("contoso cloud");

        Assert.True(deleted);
        Assert.Empty(await catalog.GetProvidersAsync());
    }

    private FileSystemProviderConnectionCatalog CreateCatalog()
        => new FileSystemProviderConnectionCatalog(Path.Combine(this._root, "providers.json"), this._protector);

    public void Dispose()
    {
        if (Directory.Exists(this._root))
        {
            Directory.Delete(this._root, recursive: true);
        }
    }

    private sealed class TestPersonalAccessTokenProtector : IPersonalAccessTokenProtector
    {
        private readonly bool _canProtect;

        public TestPersonalAccessTokenProtector(bool canProtect)
        {
            this._canProtect = canProtect;
        }

        public bool CanProtect => this._canProtect;

        public string? UnavailableReason => this._canProtect
            ? null
            : "Secure token storage is not available in this test instance. Saving a personal access token requires a supported secure store.";

        public Task<string> ProtectAsync(string personalAccessToken, string? existingProtectedPersonalAccessToken = null)
        {
            if (!this._canProtect)
            {
                throw new PlatformNotSupportedException(this.UnavailableReason);
            }

            return Task.FromResult(Convert.ToBase64String(Encoding.UTF8.GetBytes($"protected::{personalAccessToken}")));
        }

        public Task<string> UnprotectAsync(string protectedPersonalAccessToken)
        {
            string value = Encoding.UTF8.GetString(Convert.FromBase64String(protectedPersonalAccessToken));
            if (!value.StartsWith("protected::", StringComparison.Ordinal))
            {
                throw new CryptographicException("Invalid protected token.");
            }

            return Task.FromResult(value["protected::".Length..]);
        }
    }
}
