using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace ArchHarness.App.SourceControl;

/// <summary>
/// Retrieves source control data from GitHub repositories.
/// </summary>
public sealed class GitHubSourceControlService : ISourceControlReviewProviderService
{
    private const string BearerAuthorizationScheme = "Bearer";
    private const string InvalidProviderMessage = "GitHub configuration requires the GitHub provider type.";
    private const string OrganizationFieldName = "Organization";
    private const int GitHubPageSize = 100;
    private const string TokenAuthorizationScheme = "token";
    private static readonly Regex SensitiveHeaderPattern = new Regex(
        "(?i)\\b(authorization|token|pat)\\b\\s*[:=]\\s*[^,;\\s]+",
        RegexOptions.Compiled);
    private static readonly Regex SensitiveSchemePattern = new Regex(
        "(?i)\\b(Bearer|Basic)\\s+[A-Za-z0-9+/_=\\-.]+",
        RegexOptions.Compiled);
    private readonly HttpClient _httpClient;
    private readonly ILogger<GitHubSourceControlService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubSourceControlService"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client used for GitHub API calls.</param>
    public GitHubSourceControlService(HttpClient httpClient, ILogger<GitHubSourceControlService> logger)
    {
        this._httpClient = httpClient;
        this._logger = logger;
    }

    /// <inheritdoc />
    public async Task<ConnectionTestResult> TestConnectionAsync(ProviderConnectionSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            if (settings.Provider != SourceControlProvider.GitHub)
            {
                throw new InvalidOperationException(InvalidProviderMessage);
            }

            bool hasPersonalAccessToken = !string.IsNullOrWhiteSpace(settings.PersonalAccessToken);
            using HttpResponseMessage response = hasPersonalAccessToken
                ? await SendRequestAsync(BuildUserEndpoint(), settings.PersonalAccessToken, TokenAuthorizationScheme, cancellationToken)
                : await SendRequestAsync(BuildOwnerEndpoint(settings), settings.PersonalAccessToken, TokenAuthorizationScheme, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new ConnectionTestResult(false, await BuildFailureMessageAsync(response, cancellationToken));
            }

            return new ConnectionTestResult(true, "Successfully connected to GitHub.");
        }
        catch (InvalidOperationException ex)
        {
            return new ConnectionTestResult(false, ex.Message);
        }
        catch (HttpRequestException)
        {
            return new ConnectionTestResult(false, "GitHub connection failed. Unable to reach GitHub over HTTPS.");
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PullRequestSummary>> GetPullRequestsAsync(
        ProviderConnectionSettings settings,
        string? projectName,
        string? repositoryName,
        CancellationToken cancellationToken,
        string? projectFilter = null,
        string? repositoryFilter = null,
        string? authorFilter = null)
    {
        string owner = RequireValue(settings.Organization, OrganizationFieldName);
        if (!MatchesOptionalFilter(owner, projectFilter))
        {
            return Array.Empty<PullRequestSummary>();
        }

        List<PullRequestSummary> pullRequests = new List<PullRequestSummary>();
        if (!string.IsNullOrWhiteSpace(repositoryName))
        {
            string checkedRepositoryName = RequireValue(repositoryName, "repositoryName");
            if (!MatchesOptionalFilter(checkedRepositoryName, repositoryFilter))
            {
                return Array.Empty<PullRequestSummary>();
            }

            await AddPullRequestsForRepositoryAsync(settings, owner, checkedRepositoryName, authorFilter, pullRequests, cancellationToken);
            return pullRequests;
        }

        IReadOnlyList<string> repositories = await GetRepositoryNamesAsync(settings, repositoryFilter, cancellationToken);
        foreach (string currentRepository in repositories)
        {
            await AddPullRequestsForRepositoryAsync(settings, owner, currentRepository, authorFilter, pullRequests, cancellationToken);
        }

        return pullRequests;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<IReadOnlyList<PullRequestSummary>> StreamPullRequestBatchesAsync(
        ProviderConnectionSettings settings,
        string? projectName,
        string? repositoryName,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken,
        string? projectFilter = null,
        string? repositoryFilter = null,
        string? authorFilter = null)
    {
        string owner = RequireValue(settings.Organization, "Organization");
        if (!MatchesOptionalFilter(owner, projectFilter))
        {
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(repositoryName))
        {
            string checkedRepositoryName = RequireValue(repositoryName, "repositoryName");
            if (!MatchesOptionalFilter(checkedRepositoryName, repositoryFilter))
            {
                yield break;
            }

            await foreach (IReadOnlyList<PullRequestSummary> batch in GetPullRequestBatchesForRepositoryAsync(
                settings,
                owner,
                checkedRepositoryName,
                authorFilter,
                cancellationToken))
            {
                if (batch.Count > 0)
                {
                    yield return batch;
                }
            }

            yield break;
        }

        IReadOnlyList<string> repositories = await GetRepositoryNamesAsync(settings, repositoryFilter, cancellationToken);
        foreach (string currentRepository in repositories)
        {
            await foreach (IReadOnlyList<PullRequestSummary> batch in GetPullRequestBatchesForRepositoryAsync(
                settings,
                owner,
                currentRepository,
                authorFilter,
                cancellationToken))
            {
                if (batch.Count > 0)
                {
                    yield return batch;
                }
            }
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PullRequestFile>> GetPullRequestFilesAsync(
        ProviderConnectionSettings settings,
        string? projectName,
        string? repositoryName,
        string pullRequestId,
        CancellationToken cancellationToken)
    {
        string checkedRepositoryName = RequireValue(repositoryName, "repositoryName");
        string checkedPullRequestId = RequireValue(pullRequestId, "pullRequestId");
        List<PullRequestFile> files = new List<PullRequestFile>();

        int page = 1;
        bool hasMorePages = true;
        while (hasMorePages)
        {
            string requestUri = $"{BuildRepositoryEndpoint(settings, checkedRepositoryName)}/pulls/{Uri.EscapeDataString(checkedPullRequestId)}/files?per_page={GitHubPageSize}&page={page}";
            JsonDocument document = await SendArrayRequestAsync(requestUri, settings.PersonalAccessToken, cancellationToken);
            using (document)
            {
                int fileCount = 0;
                foreach (JsonElement file in document.RootElement.EnumerateArray())
                {
                    fileCount++;
                    files.Add(new PullRequestFile(
                        GetStringValue(file, "filename"),
                        PullRequestFileChangeTypes.Normalize(GetStringValue(file, "status"))));
                }

                hasMorePages = fileCount >= GitHubPageSize;
            }

            page++;
        }

        return files;
    }

    /// <inheritdoc />
    public async Task<string> GetRepositoryCloneUrlAsync(
        ProviderConnectionSettings settings,
        string? projectName,
        string repositoryName,
        CancellationToken cancellationToken)
    {
        JsonDocument document = await SendObjectRequestAsync(
            BuildRepositoryEndpoint(settings, RequireValue(repositoryName, "repositoryName")),
            settings.PersonalAccessToken,
            cancellationToken);
        using (document)
        {
            return GetStringValue(document.RootElement, "clone_url");
        }
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string requestUri, string? personalAccessToken, string authorizationScheme)
    {
        HttpRequestMessage request = new HttpRequestMessage(method, ValidateHttpsRequestUri(requestUri));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("ArchHarness", "1.0"));
        if (!string.IsNullOrWhiteSpace(personalAccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(authorizationScheme, personalAccessToken.Trim());
        }

        return request;
    }

    private static string BuildRepositoryEndpoint(ProviderConnectionSettings settings, string repositoryName)
    {
        if (settings.Provider != SourceControlProvider.GitHub)
        {
            throw new InvalidOperationException(InvalidProviderMessage);
        }

        string owner = RequireValue(settings.Organization, OrganizationFieldName);
        string escapedOwner = Uri.EscapeDataString(owner);
        string escapedRepository = Uri.EscapeDataString(repositoryName);
        return $"https://api.github.com/repos/{escapedOwner}/{escapedRepository}";
    }

    private static string BuildRepositoriesEndpoint(ProviderConnectionSettings settings)
    {
        if (settings.Provider != SourceControlProvider.GitHub)
        {
            throw new InvalidOperationException(InvalidProviderMessage);
        }

        return settings.GitHubOwnerType == GitHubOwnerType.User
            ? BuildUserRepositoriesEndpoint(settings)
            : BuildOrganizationRepositoriesEndpoint(settings);
    }

    private static string BuildOrganizationRepositoriesEndpoint(ProviderConnectionSettings settings)
    {
        if (settings.Provider != SourceControlProvider.GitHub)
        {
            throw new InvalidOperationException(InvalidProviderMessage);
        }

        string owner = RequireValue(settings.Organization, OrganizationFieldName);
        string escapedOwner = Uri.EscapeDataString(owner);
        return $"https://api.github.com/orgs/{escapedOwner}/repos";
    }

    private static string BuildUserRepositoriesEndpoint(ProviderConnectionSettings settings)
    {
        if (settings.Provider != SourceControlProvider.GitHub)
        {
            throw new InvalidOperationException(InvalidProviderMessage);
        }

        string owner = RequireValue(settings.Organization, OrganizationFieldName);
        string escapedOwner = Uri.EscapeDataString(owner);
        return $"https://api.github.com/users/{escapedOwner}/repos";
    }

    private static string BuildOwnerEndpoint(ProviderConnectionSettings settings)
        => settings.GitHubOwnerType == GitHubOwnerType.User
            ? BuildUserOwnerEndpoint(settings)
            : BuildOrganizationEndpoint(settings);

    private static string BuildOrganizationEndpoint(ProviderConnectionSettings settings)
    {
        if (settings.Provider != SourceControlProvider.GitHub)
        {
            throw new InvalidOperationException(InvalidProviderMessage);
        }

        string owner = RequireValue(settings.Organization, OrganizationFieldName);
        string escapedOwner = Uri.EscapeDataString(owner);
        return $"https://api.github.com/orgs/{escapedOwner}";
    }

    private static string BuildUserOwnerEndpoint(ProviderConnectionSettings settings)
    {
        if (settings.Provider != SourceControlProvider.GitHub)
        {
            throw new InvalidOperationException(InvalidProviderMessage);
        }

        string owner = RequireValue(settings.Organization, OrganizationFieldName);
        string escapedOwner = Uri.EscapeDataString(owner);
        return $"https://api.github.com/users/{escapedOwner}";
    }

    private static string BuildUserEndpoint()
        => new UriBuilder(Uri.UriSchemeHttps, "api.github.com")
        {
            Path = "/user"
        }.Uri.ToString();

    private static string RequireValue(string? value, string fieldName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{fieldName} is required.")
            : value.Trim();

    private static string GetStringValue(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            throw new InvalidOperationException($"GitHub response did not include the '{propertyName}' property.");
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? string.Empty,
            JsonValueKind.Number => property.GetRawText(),
            _ => property.GetRawText()
        };
    }

    private static string GetNestedStringValue(JsonElement parent, string propertyName, string nestedPropertyName)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement property) || property.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"GitHub response did not include the '{propertyName}' object.");
        }

        return GetStringValue(property, nestedPropertyName);
    }

    private static DateTimeOffset GetDateTimeOffsetValue(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property)
            || !property.TryGetDateTimeOffset(out DateTimeOffset value))
        {
            throw new InvalidOperationException($"GitHub response did not include a valid '{propertyName}' value.");
        }

        return value;
    }

    private static string GetStatus(JsonElement pullRequest)
    {
        string state = GetStringValue(pullRequest, "state");
        bool isDraft = pullRequest.TryGetProperty("draft", out JsonElement draft)
            && draft.ValueKind == JsonValueKind.True;
        return isDraft ? "draft" : state;
    }

    private static async Task<string> BuildFailureMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
        {
            return "GitHub connection failed because authentication was rejected. Verify the personal access token and required repository permissions.";
        }

        string? providerMessage = await ReadProviderMessageAsync(response, cancellationToken);
        if (string.IsNullOrWhiteSpace(providerMessage))
        {
            return $"GitHub connection failed with status code {(int)response.StatusCode} ({response.ReasonPhrase}).";
        }

        return $"GitHub connection failed: {providerMessage}";
    }

    private static async Task<string?> ReadProviderMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("message", out JsonElement message) && message.ValueKind == JsonValueKind.String)
            {
                return SanitizeProviderMessage(message.GetString());
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static bool MatchesOptionalFilter(string value, string? filter)
        => string.IsNullOrWhiteSpace(filter)
            || value.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase);

    private async Task AddPullRequestsForRepositoryAsync(
        ProviderConnectionSettings settings,
        string owner,
        string repositoryName,
        string? authorFilter,
        List<PullRequestSummary> pullRequests,
        CancellationToken cancellationToken)
    {
        await foreach (IReadOnlyList<PullRequestSummary> batch in GetPullRequestBatchesForRepositoryAsync(
            settings,
            owner,
            repositoryName,
            authorFilter,
            cancellationToken))
        {
            pullRequests.AddRange(batch);
        }
    }

    private async IAsyncEnumerable<IReadOnlyList<PullRequestSummary>> GetPullRequestBatchesForRepositoryAsync(
        ProviderConnectionSettings settings,
        string owner,
        string repositoryName,
        string? authorFilter,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        int page = 1;
        bool hasMorePages = true;
        while (hasMorePages)
        {
            string requestUri = $"{BuildRepositoryEndpoint(settings, repositoryName)}/pulls?state=open&per_page={GitHubPageSize}&page={page}";
            JsonDocument document = await SendArrayRequestAsync(requestUri, settings.PersonalAccessToken, cancellationToken);
            using (document)
            {
                int pullRequestCount = 0;
                List<PullRequestSummary> batch = new List<PullRequestSummary>();
                foreach (JsonElement pullRequest in document.RootElement.EnumerateArray())
                {
                    pullRequestCount++;
                    PullRequestSummary summary = new PullRequestSummary(
                        GetStringValue(pullRequest, "number"),
                        GetStringValue(pullRequest, "title"),
                        GetNestedStringValue(pullRequest, "user", "login"),
                        GetNestedStringValue(pullRequest, "head", "ref"),
                        GetNestedStringValue(pullRequest, "base", "ref"),
                        GetStatus(pullRequest),
                        owner,
                        repositoryName,
                        GetStringValue(pullRequest, "html_url"),
                        GetDateTimeOffsetValue(pullRequest, "created_at"));

                    if (MatchesOptionalFilter(summary.Author, authorFilter))
                    {
                        batch.Add(summary);
                    }
                }

                if (batch.Count > 0)
                {
                    yield return batch;
                }

                hasMorePages = pullRequestCount >= GitHubPageSize;
            }

            page++;
        }
    }

    private async Task<IReadOnlyList<string>> GetRepositoryNamesAsync(
        ProviderConnectionSettings settings,
        string? repositoryFilter,
        CancellationToken cancellationToken)
    {
        List<string> repositories = new List<string>();
        int page = 1;
        bool hasMorePages = true;
        while (hasMorePages)
        {
            string requestUri = $"{BuildRepositoriesEndpoint(settings)}?type=all&per_page={GitHubPageSize}&page={page}";
            JsonDocument document = await SendArrayRequestAsync(requestUri, settings.PersonalAccessToken, cancellationToken);
            using (document)
            {
                int repositoryCount = 0;
                foreach (JsonElement repository in document.RootElement.EnumerateArray())
                {
                    repositoryCount++;
                    string repositoryName = GetStringValue(repository, "name");
                    if (MatchesOptionalFilter(repositoryName, repositoryFilter))
                    {
                        repositories.Add(repositoryName);
                    }
                }

                hasMorePages = repositoryCount >= GitHubPageSize;
            }

            page++;
        }

        return repositories;
    }

    private async Task<HttpResponseMessage> SendRequestAsync(
        string requestUri,
        string? personalAccessToken,
        string authorizationScheme,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Sending GitHub API request to {RequestUri}.", requestUri);
        HttpRequestMessage request = CreateRequest(HttpMethod.Get, requestUri, personalAccessToken, authorizationScheme);
        try
        {
            return await this._httpClient.SendAsync(request, cancellationToken);
        }
        catch
        {
            request.Dispose();
            throw;
        }
    }

    private async Task<JsonDocument> SendArrayRequestAsync(string requestUri, string? personalAccessToken, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendRequestAsync(requestUri, personalAccessToken, BearerAuthorizationScheme, cancellationToken);
        return await ParseArrayResponseAsync(response, "pull request data retrieval", cancellationToken);
    }

    private async Task<JsonDocument> SendObjectRequestAsync(string requestUri, string? personalAccessToken, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendRequestAsync(requestUri, personalAccessToken, BearerAuthorizationScheme, cancellationToken);
        await EnsureSuccessStatusCodeAsync(response, "repository metadata retrieval", cancellationToken);

        await using Stream contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        JsonDocument document = await JsonDocument.ParseAsync(contentStream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            document.Dispose();
            throw new InvalidOperationException("GitHub response did not include a valid object payload.");
        }

        return document;
    }

    private static async Task<JsonDocument> ParseArrayResponseAsync(HttpResponseMessage response, string operationName, CancellationToken cancellationToken)
    {
        await EnsureSuccessStatusCodeAsync(response, operationName, cancellationToken);

        await using Stream contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        JsonDocument document = await JsonDocument.ParseAsync(contentStream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            document.Dispose();
            throw new InvalidOperationException("GitHub response did not include a valid array payload.");
        }

        return document;
    }

    private static async Task EnsureSuccessStatusCodeAsync(HttpResponseMessage response, string operationName, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw new SourceControlRequestFailedException(
            await BuildRequestFailureMessageAsync(operationName, response, cancellationToken),
            response.StatusCode);
    }

    private static async Task<string> BuildRequestFailureMessageAsync(string operationName, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
        {
            return $"GitHub {operationName} failed because authentication was rejected. Verify the personal access token and required repository permissions.";
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return $"GitHub {operationName} failed because the requested resource was not found.";
        }

        string? providerMessage = await ReadProviderMessageAsync(response, cancellationToken);
        if (string.IsNullOrWhiteSpace(providerMessage))
        {
            return $"GitHub {operationName} failed with status code {(int)response.StatusCode} ({response.ReasonPhrase}).";
        }

        return $"GitHub {operationName} failed: {providerMessage}";
    }

    private static Uri ValidateHttpsRequestUri(string requestUri)
    {
        if (!Uri.TryCreate(requestUri, UriKind.Absolute, out Uri? uri) || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("GitHub API requests must use HTTPS.");
        }

        return uri;
    }

    private static string? SanitizeProviderMessage(string? providerMessage)
    {
        if (string.IsNullOrWhiteSpace(providerMessage))
        {
            return null;
        }

        string sanitized = providerMessage
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        sanitized = SensitiveSchemePattern.Replace(sanitized, "$1 [REDACTED]");
        sanitized = SensitiveHeaderPattern.Replace(sanitized, "$1=[REDACTED]");
        return sanitized.Length <= 240 ? sanitized : sanitized[..240];
    }
}
