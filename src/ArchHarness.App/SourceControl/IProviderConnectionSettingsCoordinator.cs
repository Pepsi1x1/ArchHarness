using ArchHarness.App.Storage;

namespace ArchHarness.App.SourceControl;

/// <summary>
/// Normalizes, hydrates, and validates source-control provider connection settings.
/// </summary>
public interface IProviderConnectionSettingsCoordinator
{
    /// <summary>
    /// Normalizes and hydrates settings for a connectivity test.
    /// </summary>
    Task<ProviderConnectionSettings> PrepareForConnectionTestAsync(ProviderConnectionSettings settings);

    /// <summary>
    /// Normalizes and hydrates settings for persistence.
    /// </summary>
    Task<ProviderConnectionSettings> PrepareForSaveAsync(ProviderConnectionSettings settings);

    /// <summary>
    /// Returns field-level validation errors for the supplied settings.
    /// </summary>
    Dictionary<string, string[]> GetValidationErrors(ProviderConnectionSettings settings, bool requirePersonalAccessToken);

    /// <summary>
    /// Validates settings and throws when invalid.
    /// </summary>
    void ValidateOrThrow(ProviderConnectionSettings settings, bool requirePersonalAccessToken);
}

/// <summary>
/// Default implementation of <see cref="IProviderConnectionSettingsCoordinator"/>.
/// </summary>
public sealed class ProviderConnectionSettingsCoordinator : IProviderConnectionSettingsCoordinator
{
    private static readonly char[] _invalidDisplayNameCharacters = new[] { '/', '\\' };

    private readonly IProviderConnectionCatalog _providerConnectionCatalog;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderConnectionSettingsCoordinator"/> class.
    /// </summary>
    public ProviderConnectionSettingsCoordinator(IProviderConnectionCatalog providerConnectionCatalog)
    {
        this._providerConnectionCatalog = providerConnectionCatalog;
    }

    /// <inheritdoc />
    public async Task<ProviderConnectionSettings> PrepareForConnectionTestAsync(ProviderConnectionSettings settings)
        => HydrateTestCredential(Normalize(settings), await this.FindExistingProviderAsync(settings.DisplayName).ConfigureAwait(false));

    /// <inheritdoc />
    public async Task<ProviderConnectionSettings> PrepareForSaveAsync(ProviderConnectionSettings settings)
        => HydrateSavedCredential(Normalize(settings), await this.FindExistingProviderAsync(settings.DisplayName).ConfigureAwait(false));

    /// <inheritdoc />
    public Dictionary<string, string[]> GetValidationErrors(ProviderConnectionSettings settings, bool requirePersonalAccessToken)
    {
        Dictionary<string, List<string>> errors = new(StringComparer.OrdinalIgnoreCase);

        if (!Enum.IsDefined(settings.Provider))
        {
            AddError(errors, "provider", "Provider is required.");
        }

        ValidateDisplayName(settings, errors);

        const string ORGANIZATION_KEY = "organization";

        if (string.IsNullOrWhiteSpace(settings.Organization))
        {
            AddError(errors, ORGANIZATION_KEY, "Organization is required.");
        }

        ValidateGithub(settings, errors);

        if (settings.Provider == SourceControlProvider.AzureDevOpsServer)
        {
            ValidateAzureDevOpsServer(settings, errors);
        }

        ValidatePersonalAccessToken(settings, requirePersonalAccessToken, errors);

        return errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
    }

    private static void ValidatePersonalAccessToken(ProviderConnectionSettings settings, bool requirePersonalAccessToken, Dictionary<string, List<string>> errors)
    {
        const string PERSONAL_ACCESS_TOKEN_KEY = "personalAccessToken";

        if (settings.ClearPersonalAccessToken && !string.IsNullOrWhiteSpace(settings.PersonalAccessToken))
        {
            AddError(errors, PERSONAL_ACCESS_TOKEN_KEY, "Provide a personal access token or clear the stored token, but not both.");
        }
        else if (requirePersonalAccessToken && string.IsNullOrWhiteSpace(settings.PersonalAccessToken))
        {
            AddError(errors, PERSONAL_ACCESS_TOKEN_KEY, "PersonalAccessToken is required.");
        }
        else if (LooksLikeAbsoluteHttpUrl(settings.PersonalAccessToken))
        {
            AddError(errors, PERSONAL_ACCESS_TOKEN_KEY, "PersonalAccessToken looks like a URL. Check browser autofill and re-enter the token.");
        }
    }

    private static void ValidateAzureDevOpsServer(ProviderConnectionSettings settings, Dictionary<string, List<string>> errors)
    {
        const string SERVER_URL_KEY = "serverUrl";
        if (string.IsNullOrWhiteSpace(settings.ServerUrl))
        {
            AddError(errors, SERVER_URL_KEY, "ServerUrl is required for Azure DevOps Server.");
        }
        else if (!Uri.TryCreate(settings.ServerUrl, UriKind.Absolute, out Uri? parsedServerUrl))
        {
            AddError(errors, SERVER_URL_KEY, "ServerUrl must be an absolute URL.");
        }
        else if (!string.Equals(parsedServerUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            AddError(errors, SERVER_URL_KEY, "ServerUrl must use HTTPS.");
        }
        else if (!string.IsNullOrEmpty(parsedServerUrl.UserInfo))
        {
            AddError(errors, SERVER_URL_KEY, "ServerUrl cannot include embedded credentials.");
        }
    }

    private static void ValidateGithub(ProviderConnectionSettings settings, Dictionary<string, List<string>> errors)
    {
        const string GITHUB_OWNER_TYPE_KEY = "gitHubOwnerType";
        if (settings.Provider == SourceControlProvider.GitHub && !Enum.IsDefined(settings.GitHubOwnerType))
        {
            AddError(errors, GITHUB_OWNER_TYPE_KEY, "GitHubOwnerType is required for GitHub providers.");
        }

        const string GITHUB_AUTHENTICATION_MODE_KEY = "gitHubAuthenticationMode";
        if (settings.Provider == SourceControlProvider.GitHub && !Enum.IsDefined(settings.GitHubAuthenticationMode))
        {
            AddError(errors, GITHUB_AUTHENTICATION_MODE_KEY, "GitHubAuthenticationMode is invalid for GitHub providers.");
        }
    }

    private static void ValidateDisplayName(ProviderConnectionSettings settings, Dictionary<string, List<string>> errors)
    {
        const string DISPLAY_NAME_KEY = "displayName";

        if (string.IsNullOrWhiteSpace(settings.DisplayName))
        {
            AddError(errors, DISPLAY_NAME_KEY, "DisplayName is required.");
        }
        else if (settings.DisplayName.IndexOfAny(_invalidDisplayNameCharacters) >= 0)
        {
            AddError(errors, DISPLAY_NAME_KEY, "DisplayName cannot contain path separator characters.");
        }
    }

    /// <inheritdoc />
    public void ValidateOrThrow(ProviderConnectionSettings settings, bool requirePersonalAccessToken)
    {
        Dictionary<string, string[]> errors = this.GetValidationErrors(settings, requirePersonalAccessToken);
        if (errors.Count == 0)
        {
            return;
        }

        string message = string.Join(
            " ",
            errors
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .SelectMany(pair => pair.Value)
                .Distinct(StringComparer.Ordinal));

        throw new InvalidOperationException(message);
    }

    private static ProviderConnectionSettings Normalize(ProviderConnectionSettings settings)
        => settings with
        {
            DisplayName = NormalizeText(settings.DisplayName),
            ServerUrl = settings.Provider == SourceControlProvider.AzureDevOpsServer ? NormalizeText(settings.ServerUrl) : null,
            Organization = NormalizeText(settings.Organization),
            GitHubAuthenticatedUser = settings.Provider == SourceControlProvider.GitHub ? NormalizeText(settings.GitHubAuthenticatedUser) : null,
            PersonalAccessToken = NormalizeText(settings.PersonalAccessToken),
            GitHubAuthenticationMode = settings.Provider == SourceControlProvider.GitHub && Enum.IsDefined(settings.GitHubAuthenticationMode)
                ? settings.GitHubAuthenticationMode
                : GitHubAuthenticationMode.None,
            RetainPersonalAccessToken = settings.Provider == SourceControlProvider.GitHub && settings.RetainPersonalAccessToken && !settings.ClearPersonalAccessToken,
            ClearPersonalAccessToken = settings.ClearPersonalAccessToken
        };

    private async Task<ProviderConnectionSettings?> FindExistingProviderAsync(string? displayName)
    {
        string? normalizedDisplayName = NormalizeText(displayName);
        return string.IsNullOrWhiteSpace(normalizedDisplayName)
            ? null
            : (await this._providerConnectionCatalog
                .GetProvidersAsync()
                .ConfigureAwait(false))
                .FirstOrDefault(provider => string.Equals(provider.DisplayName, normalizedDisplayName, StringComparison.OrdinalIgnoreCase));
    }

    private static ProviderConnectionSettings HydrateTestCredential(ProviderConnectionSettings settings, ProviderConnectionSettings? existing)
    {
        if (settings.ClearPersonalAccessToken)
        {
            return ClearStoredCredential(settings, existing);
        }

        if (!string.IsNullOrWhiteSpace(settings.PersonalAccessToken))
        {
            return FinalizeGitHubCredentialMetadata(settings);
        }

        if (settings.Provider == SourceControlProvider.GitHub && settings.RetainPersonalAccessToken && existing is not null)
        {
            return settings with
            {
                PersonalAccessToken = existing.PersonalAccessToken,
                PersonalAccessTokenStorageMode = existing.PersonalAccessTokenStorageMode,
                GitHubAuthenticationMode = existing.GitHubAuthenticationMode,
                GitHubAuthenticatedUser = existing.GitHubAuthenticatedUser
            };
        }

        return FinalizeGitHubCredentialMetadata(settings);
    }

    private static ProviderConnectionSettings HydrateSavedCredential(ProviderConnectionSettings settings, ProviderConnectionSettings? existing)
    {
        if (settings.ClearPersonalAccessToken)
        {
            return ClearStoredCredential(settings, existing);
        }

        if (string.IsNullOrWhiteSpace(settings.PersonalAccessToken))
        {
            if (settings.Provider == SourceControlProvider.GitHub)
            {
                if (settings.RetainPersonalAccessToken && existing is not null)
                {
                    return settings with
                    {
                        PersonalAccessToken = existing.PersonalAccessToken,
                        PersonalAccessTokenStorageMode = existing.PersonalAccessTokenStorageMode,
                        GitHubAuthenticationMode = existing.GitHubAuthenticationMode,
                        GitHubAuthenticatedUser = existing.GitHubAuthenticatedUser
                    };
                }

                return settings with
                {
                    PersonalAccessToken = null,
                    GitHubAuthenticationMode = GitHubAuthenticationMode.None,
                    GitHubAuthenticatedUser = null
                };
            }

            return settings with
            {
                PersonalAccessToken = existing?.PersonalAccessToken,
                PersonalAccessTokenStorageMode = existing?.PersonalAccessTokenStorageMode ?? settings.PersonalAccessTokenStorageMode
            };
        }

        return FinalizeGitHubCredentialMetadata(settings);
    }

    private static ProviderConnectionSettings ClearStoredCredential(ProviderConnectionSettings settings, ProviderConnectionSettings? existing)
    {
        ProviderConnectionSettings cleared = settings with
        {
            PersonalAccessToken = null,
            PersonalAccessTokenStorageMode = existing?.PersonalAccessTokenStorageMode ?? settings.PersonalAccessTokenStorageMode,
            RetainPersonalAccessToken = false
        };

        if (settings.Provider != SourceControlProvider.GitHub)
        {
            return cleared;
        }

        return cleared with
        {
            GitHubAuthenticationMode = GitHubAuthenticationMode.None,
            GitHubAuthenticatedUser = null
        };
    }

    private static ProviderConnectionSettings FinalizeGitHubCredentialMetadata(ProviderConnectionSettings settings)
    {
        if (settings.Provider != SourceControlProvider.GitHub)
        {
            return settings with
            {
                GitHubAuthenticationMode = GitHubAuthenticationMode.None,
                GitHubAuthenticatedUser = null,
                RetainPersonalAccessToken = false
            };
        }

        GitHubAuthenticationMode mode = settings.GitHubAuthenticationMode;
        if (!string.IsNullOrWhiteSpace(settings.PersonalAccessToken) && mode == GitHubAuthenticationMode.None)
        {
            mode = GitHubAuthenticationMode.PersonalAccessToken;
        }

        return settings with
        {
            GitHubAuthenticationMode = mode,
            GitHubAuthenticatedUser = mode == GitHubAuthenticationMode.OAuthDeviceFlow ? settings.GitHubAuthenticatedUser : null
        };
    }

    private static string? NormalizeText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool LooksLikeAbsoluteHttpUrl(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && Uri.TryCreate(value, UriKind.Absolute, out Uri? parsedUri)
            && (string.Equals(parsedUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(parsedUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));

    private static void AddError(IDictionary<string, List<string>> errors, string key, string message)
    {
        if (!errors.TryGetValue(key, out List<string>? values))
        {
            values = new List<string>();
            errors[key] = values;
        }

        values.Add(message);
    }
}
