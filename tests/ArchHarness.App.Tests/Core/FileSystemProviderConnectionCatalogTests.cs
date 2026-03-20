using System.Security.Cryptography;
using System.Text;
using ArchHarness.App.SourceControl;
using ArchHarness.App.Storage;

namespace ArchHarness.App.Tests.Core;

public sealed class FileSystemProviderConnectionCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ArchHarnessProviderConnectionTests", Guid.NewGuid().ToString("N"));
    private readonly TestPersonalAccessTokenProtector _protector = new TestPersonalAccessTokenProtector(canProtect: true);

    [Fact]
    public void SaveProvider_PersistsEncryptedPersonalAccessToken()
    {
        FileSystemProviderConnectionCatalog catalog = this.CreateCatalog();

        catalog.SaveProvider(new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.GitHub,
            DisplayName = "GitHub",
            Organization = "octo-org",
            PersonalAccessToken = "github-pat",
            IsEnabled = true
        });

        string json = File.ReadAllText(Path.Combine(this._root, "providers.json"));
        Assert.DoesNotContain("github-pat", json);

        ProviderConnectionSettings persisted = Assert.Single(this.CreateCatalog().GetProviders());
        Assert.Equal("github-pat", persisted.PersonalAccessToken);
        Assert.Equal(PersonalAccessTokenStorageMode.Protected, persisted.PersonalAccessTokenStorageMode);
    }

    [Fact]
    public void SaveProvider_PersistsPlainTextPersonalAccessTokenWhenRequested()
    {
        FileSystemProviderConnectionCatalog catalog = this.CreateCatalog();

        catalog.SaveProvider(new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.GitHub,
            DisplayName = "GitHub",
            Organization = "octo-org",
            PersonalAccessToken = "github-pat",
            PersonalAccessTokenStorageMode = PersonalAccessTokenStorageMode.PlainText,
            IsEnabled = true
        });

        string json = File.ReadAllText(Path.Combine(this._root, "providers.json"));
        Assert.Contains("github-pat", json);

        ProviderConnectionSettings persisted = Assert.Single(this.CreateCatalog().GetProviders());
        Assert.Equal(PersonalAccessTokenStorageMode.PlainText, persisted.PersonalAccessTokenStorageMode);
    }

    [Fact]
    public void SaveProvider_PersistsGitHubOwnerType()
    {
        FileSystemProviderConnectionCatalog catalog = this.CreateCatalog();

        catalog.SaveProvider(new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.GitHub,
            DisplayName = "GitHub User",
            Organization = "octocat",
            GitHubOwnerType = GitHubOwnerType.User,
            PersonalAccessToken = null,
            IsEnabled = true
        });

        ProviderConnectionSettings persisted = Assert.Single(this.CreateCatalog().GetProviders());
        Assert.Equal(GitHubOwnerType.User, persisted.GitHubOwnerType);
    }

    [Fact]
    public void SaveProvider_PersistsGitHubOAuthMetadata()
    {
        FileSystemProviderConnectionCatalog catalog = this.CreateCatalog();

        catalog.SaveProvider(new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.GitHub,
            DisplayName = "GitHub OAuth",
            Organization = "octo-org",
            GitHubAuthenticationMode = GitHubAuthenticationMode.OAuthDeviceFlow,
            GitHubAuthenticatedUser = "octocat",
            PersonalAccessToken = "oauth-token",
            IsEnabled = true
        });

        ProviderConnectionSettings persisted = Assert.Single(this.CreateCatalog().GetProviders());
        Assert.Equal(GitHubAuthenticationMode.OAuthDeviceFlow, persisted.GitHubAuthenticationMode);
        Assert.Equal("octocat", persisted.GitHubAuthenticatedUser);
        Assert.Equal("oauth-token", persisted.PersonalAccessToken);
    }

    [Fact]
    public void SaveProvider_ThrowsWhenProtectedStorageIsUnavailable()
    {
        FileSystemProviderConnectionCatalog catalog = new FileSystemProviderConnectionCatalog(
            Path.Combine(this._root, "providers.json"),
            new TestPersonalAccessTokenProtector(canProtect: false));

        PlainTextPersonalAccessTokenConfirmationRequiredException ex = Assert.Throws<PlainTextPersonalAccessTokenConfirmationRequiredException>(() =>
            catalog.SaveProvider(new ProviderConnectionSettings
            {
                Provider = SourceControlProvider.GitHub,
                DisplayName = "GitHub",
                Organization = "octo-org",
                PersonalAccessToken = "github-pat",
                IsEnabled = true
            }));

        Assert.Contains("plain text", ex.WarningMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeleteProvider_RemovesMatchingProviderIgnoringCase()
    {
        FileSystemProviderConnectionCatalog catalog = this.CreateCatalog();
        catalog.SaveProvider(new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.AzureDevOpsServices,
            DisplayName = "Contoso Cloud",
            Organization = "contoso",
            PersonalAccessToken = "ado-pat",
            IsEnabled = true
        });

        bool deleted = catalog.DeleteProvider("contoso cloud");

        Assert.True(deleted);
        Assert.Empty(catalog.GetProviders());
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
            : "Secure token storage is not available in this test instance. Storing the token will write it to disk in plain text.";

        public string Protect(string personalAccessToken, string? existingProtectedPersonalAccessToken = null)
        {
            if (!this._canProtect)
            {
                throw new PlatformNotSupportedException(this.UnavailableReason);
            }

            return Convert.ToBase64String(Encoding.UTF8.GetBytes($"protected::{personalAccessToken}"));
        }

        public string Unprotect(string protectedPersonalAccessToken)
        {
            string value = Encoding.UTF8.GetString(Convert.FromBase64String(protectedPersonalAccessToken));
            if (!value.StartsWith("protected::", StringComparison.Ordinal))
            {
                throw new CryptographicException("Invalid protected token.");
            }

            return value["protected::".Length..];
        }
    }
}
