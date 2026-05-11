using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ArchHarness.App.SourceControl;

/// <summary>
/// Retrieves source control data from Azure DevOps Server and Azure DevOps Services.
/// </summary>
public sealed class AzureDevOpsSourceControlService : ISourceControlReviewProviderService
{
    private const char URL_PATH_SEPARATOR = '/';
    private const string VALUE_PROPERTY_NAME = "value";
    private static readonly Regex _sensitiveHeaderPattern = new Regex(
        "(?i)\\b(authorization|token|pat)\\b\\s*[:=]\\s*[^,;\\s]+",
        RegexOptions.Compiled);
    private static readonly Regex _sensitiveSchemePattern = new Regex(
        "(?i)\\b(Bearer|Basic)\\s+[A-Za-z0-9+/_=\\-.]+",
        RegexOptions.Compiled);
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureDevOpsSourceControlService"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client used for Azure DevOps API calls.</param>
    public AzureDevOpsSourceControlService(HttpClient httpClient)
    {
        this._httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<ConnectionTestResult> TestConnectionAsync(ProviderConnectionSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            string requestUri = BuildProjectsEndpoint(settings);
            using HttpRequestMessage request = CreateRequest(HttpMethod.Get, requestUri, settings.PersonalAccessToken);
            using HttpResponseMessage response = await this._httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new ConnectionTestResult(false, await BuildFailureMessageAsync("Azure DevOps", response, cancellationToken));
            }

            return new ConnectionTestResult(true, "Successfully connected to Azure DevOps.");
        }
        catch (InvalidOperationException ex)
        {
            return new ConnectionTestResult(false, ex.Message);
        }
        catch (HttpRequestException)
        {
            return new ConnectionTestResult(false, "Azure DevOps connection failed. Unable to reach the server over HTTPS.");
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
        if (!string.IsNullOrWhiteSpace(projectName))
        {
            return await this.GetPullRequestsForExplicitProjectAsync(
                settings,
                projectName,
                repositoryName,
                projectFilter,
                repositoryFilter,
                authorFilter,
                cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(projectFilter))
        {
            return await this.GetPullRequestsForFilteredProjectAsync(
                settings,
                projectFilter,
                repositoryName,
                repositoryFilter,
                authorFilter,
                cancellationToken);
        }

        return await this.GetPullRequestsAcrossProjectsAsync(
            settings,
            repositoryName,
            repositoryFilter,
            authorFilter,
            cancellationToken);
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
        if (!string.IsNullOrWhiteSpace(projectName))
        {
            await foreach (IReadOnlyList<PullRequestSummary> batch in this.StreamPullRequestsForExplicitProjectAsync(
                settings,
                projectName,
                repositoryName,
                projectFilter,
                repositoryFilter,
                authorFilter,
                cancellationToken))
            {
                yield return batch;
            }

            yield break;
        }

        if (!string.IsNullOrWhiteSpace(projectFilter))
        {
            await foreach (IReadOnlyList<PullRequestSummary> batch in this.StreamPullRequestsForFilteredProjectAsync(
                settings,
                projectFilter,
                repositoryName,
                repositoryFilter,
                authorFilter,
                cancellationToken))
            {
                yield return batch;
            }

            yield break;
        }

        await foreach (IReadOnlyList<PullRequestSummary> batch in this.StreamPullRequestsAcrossProjectsAsync(
            settings,
            repositoryName,
            repositoryFilter,
            authorFilter,
            cancellationToken))
        {
            yield return batch;
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
        string checkedProjectName = RequireValue(projectName, "projectName");
        string checkedRepositoryName = RequireValue(repositoryName, "repositoryName");
        string checkedPullRequestId = RequireValue(pullRequestId, "pullRequestId");
        int latestIterationId = await this.GetLatestIterationIdAsync(settings, checkedProjectName, checkedRepositoryName, checkedPullRequestId, cancellationToken);

        string requestUri = BuildPullRequestIterationChangesEndpoint(settings, checkedProjectName, checkedRepositoryName, checkedPullRequestId, latestIterationId);
        using HttpRequestMessage request = CreateRequest(HttpMethod.Get, requestUri, settings.PersonalAccessToken);
        using HttpResponseMessage response = await this._httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessStatusCodeAsync(response, "pull request file retrieval", cancellationToken);

        await using Stream contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(contentStream, cancellationToken: cancellationToken);
        JsonElement changeEntries = GetChangeEntries(document.RootElement);

        List<PullRequestFile> files = new List<PullRequestFile>();
        foreach (JsonElement changeEntry in changeEntries.EnumerateArray())
        {
            files.Add(new PullRequestFile(
                GetChangePath(changeEntry),
                PullRequestFileChangeTypes.Normalize(GetStringValue(changeEntry, "changeType"))));
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
        string checkedProjectName = RequireValue(projectName, "projectName");
        string checkedRepositoryName = RequireValue(repositoryName, "repositoryName");
        string requestUri = BuildRepositoryEndpoint(settings, checkedProjectName, checkedRepositoryName);
        using HttpRequestMessage request = CreateRequest(HttpMethod.Get, requestUri, settings.PersonalAccessToken);
        using HttpResponseMessage response = await this._httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessStatusCodeAsync(response, "repository metadata retrieval", cancellationToken);

        await using Stream contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(contentStream, cancellationToken: cancellationToken);
        return GetStringValue(document.RootElement, "remoteUrl");
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string requestUri, string? personalAccessToken)
    {
        HttpRequestMessage request = new HttpRequestMessage(method, ValidateHttpsRequestUri(requestUri));
        if (!string.IsNullOrWhiteSpace(personalAccessToken))
        {
            byte[] tokenBytes = Encoding.ASCII.GetBytes($":{personalAccessToken.Trim()}");
            string encodedToken = Convert.ToBase64String(tokenBytes);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encodedToken);
        }

        return request;
    }

    private static string BuildPullRequestsEndpoint(ProviderConnectionSettings settings, string? projectName, string repositoryName)
    {
        string checkedProjectName = RequireValue(projectName, "projectName");
        string checkedRepositoryName = RequireValue(repositoryName, "repositoryName");
        string escapedRepositoryName = Uri.EscapeDataString(checkedRepositoryName);
        return $"{BuildBaseEndpoint(settings, checkedProjectName)}/_apis/git/repositories/{escapedRepositoryName}/pullrequests?api-version=7.0&searchCriteria.status=active";
    }

    private static string BuildRepositoriesEndpoint(ProviderConnectionSettings settings, string projectName)
    {
        string checkedProjectName = RequireValue(projectName, "projectName");
        return $"{BuildBaseEndpoint(settings, checkedProjectName)}/_apis/git/repositories?api-version=7.0";
    }

    private static string BuildRepositoryEndpoint(ProviderConnectionSettings settings, string projectName, string repositoryName)
    {
        string checkedProjectName = RequireValue(projectName, "projectName");
        string checkedRepositoryName = RequireValue(repositoryName, "repositoryName");
        string escapedRepositoryName = Uri.EscapeDataString(checkedRepositoryName);
        return $"{BuildBaseEndpoint(settings, checkedProjectName)}/_apis/git/repositories/{escapedRepositoryName}?api-version=7.0";
    }

    private static string BuildPullRequestIterationsEndpoint(ProviderConnectionSettings settings, string projectName, string repositoryName, string pullRequestId)
    {
        string checkedProjectName = RequireValue(projectName, "projectName");
        string checkedRepositoryName = RequireValue(repositoryName, "repositoryName");
        string checkedPullRequestId = RequireValue(pullRequestId, "pullRequestId");
        string escapedRepositoryName = Uri.EscapeDataString(checkedRepositoryName);
        string escapedPullRequestId = Uri.EscapeDataString(checkedPullRequestId);
        return $"{BuildBaseEndpoint(settings, checkedProjectName)}/_apis/git/repositories/{escapedRepositoryName}/pullRequests/{escapedPullRequestId}/iterations?api-version=7.0";
    }

    private static string BuildPullRequestIterationChangesEndpoint(ProviderConnectionSettings settings, string projectName, string repositoryName, string pullRequestId, int iterationId)
    {
        string checkedProjectName = RequireValue(projectName, "projectName");
        string checkedRepositoryName = RequireValue(repositoryName, "repositoryName");
        string checkedPullRequestId = RequireValue(pullRequestId, "pullRequestId");
        string escapedRepositoryName = Uri.EscapeDataString(checkedRepositoryName);
        string escapedPullRequestId = Uri.EscapeDataString(checkedPullRequestId);
        return $"{BuildBaseEndpoint(settings, checkedProjectName)}/_apis/git/repositories/{escapedRepositoryName}/pullRequests/{escapedPullRequestId}/iterations/{iterationId}/changes?api-version=7.0";
    }

    private static string BuildProjectsEndpoint(ProviderConnectionSettings settings)
    {
        return $"{BuildBaseEndpoint(settings)}/_apis/projects?api-version=6.0";
    }

    private static string BuildBaseEndpoint(ProviderConnectionSettings settings, params string[] pathSegments)
    {
        string organization = RequireValue(settings.Organization, "Organization");
        return BuildBaseEndpoint(settings.Provider, settings.ServerUrl, organization, pathSegments);
    }

    private static string BuildBaseEndpoint(SourceControlProvider providerType, string? serverUrl, string organization, params string[] pathSegments)
    {
        switch (providerType)
        {
            case SourceControlProvider.AzureDevOpsServer:
                {
                    string checkedServerUrl = RequireValue(serverUrl, "ServerUrl");
                    return BuildServerEndpoint(checkedServerUrl, organization, pathSegments);
                }

            case SourceControlProvider.AzureDevOpsServices:
                {
                    string escapedOrganization = Uri.EscapeDataString(organization);
                    StringBuilder builder = new StringBuilder($"https://dev.azure.com/{escapedOrganization}");
                    foreach (string segment in pathSegments)
                    {
                        builder.Append('/');
                        builder.Append(Uri.EscapeDataString(RequireValue(segment, "segment")));
                    }

                    return builder.ToString();
                }

            default:
                throw new InvalidOperationException("Azure DevOps configuration requires an Azure DevOps provider type.");
        }
    }

    private static string BuildServerEndpoint(string serverUrl, string collectionName, params string[] pathSegments)
    {
        Uri serverUri = ValidateHttpsRequestUri(serverUrl);
        List<string> segments = serverUri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToList();

        if (!EndsWithSegment(segments, collectionName))
        {
            segments.Add(collectionName);
        }

        foreach (string segment in pathSegments)
        {
            segments.Add(RequireValue(segment, "segment"));
        }

        UriBuilder builder = new UriBuilder(serverUri)
        {
            Path = BuildUrlPath(segments)
        };

        return builder.Uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    private static bool EndsWithSegment(IReadOnlyList<string> segments, string segment)
    {
        if (segments.Count == 0)
        {
            return false;
        }

        return string.Equals(segments[^1], segment, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildUrlPath(IReadOnlyList<string> segments)
    {
        if (segments.Count == 0)
        {
            return URL_PATH_SEPARATOR.ToString();
        }

        StringBuilder builder = new StringBuilder();
        foreach (string segment in segments)
        {
            builder.Append(URL_PATH_SEPARATOR);
            builder.Append(Uri.EscapeDataString(RequireValue(segment, "segment")));
        }

        return builder.ToString();
    }

    private static string RequireValue(string? value, string fieldName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{fieldName} is required.")
            : value.Trim();

    private static JsonElement GetArrayProperty(JsonElement parent, string propertyName, string description)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Azure DevOps response did not include a valid {description} array.");
        }

        return value;
    }

    private static JsonElement GetChangeEntries(JsonElement parent)
    {
        if (parent.TryGetProperty("changeEntries", out JsonElement changeEntries) && changeEntries.ValueKind == JsonValueKind.Array)
        {
            return changeEntries;
        }

        if (parent.TryGetProperty("changes", out JsonElement changes) && changes.ValueKind == JsonValueKind.Array)
        {
            return changes;
        }

        return GetArrayProperty(parent, VALUE_PROPERTY_NAME, "pull request file changes");
    }

    private static string GetStringValue(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            throw new InvalidOperationException($"Azure DevOps response did not include the '{propertyName}' property.");
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
            throw new InvalidOperationException($"Azure DevOps response did not include the '{propertyName}' object.");
        }

        return GetStringValue(property, nestedPropertyName);
    }

    private static string GetChangePath(JsonElement changeEntry)
    {
        if (changeEntry.TryGetProperty("item", out JsonElement item)
            && item.ValueKind == JsonValueKind.Object
            && item.TryGetProperty("path", out JsonElement path)
            && path.ValueKind == JsonValueKind.String)
        {
            return path.GetString() ?? string.Empty;
        }

        if (changeEntry.TryGetProperty("originalPath", out JsonElement originalPath) && originalPath.ValueKind == JsonValueKind.String)
        {
            return originalPath.GetString() ?? string.Empty;
        }

        throw new InvalidOperationException("Azure DevOps response did not include a valid file path for a pull request change.");
    }

    private static DateTimeOffset GetDateTimeOffsetValue(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property)
            || !property.TryGetDateTimeOffset(out DateTimeOffset value))
        {
            throw new InvalidOperationException($"Azure DevOps response did not include a valid '{propertyName}' value.");
        }

        return value;
    }

    private static string GetPullRequestUrl(JsonElement pullRequest)
    {
        if (pullRequest.TryGetProperty("_links", out JsonElement links)
            && links.ValueKind == JsonValueKind.Object
            && links.TryGetProperty("web", out JsonElement web)
            && web.ValueKind == JsonValueKind.Object
            && web.TryGetProperty("href", out JsonElement href)
            && href.ValueKind == JsonValueKind.String)
        {
            return href.GetString() ?? string.Empty;
        }

        return GetStringValue(pullRequest, "url");
    }

    private static string NormalizeBranchName(string branchName)
        => branchName.StartsWith("refs/heads/", StringComparison.OrdinalIgnoreCase)
            ? branchName["refs/heads/".Length..]
            : branchName;

    private static async Task<string> BuildFailureMessageAsync(string providerName, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
        {
            return $"{providerName} connection failed because authentication was rejected. Verify the personal access token and required repository permissions.";
        }

        string? providerMessage = await ReadProviderMessageAsync(response, cancellationToken);
        if (string.IsNullOrWhiteSpace(providerMessage))
        {
            return $"{providerName} connection failed with status code {(int)response.StatusCode} ({response.ReasonPhrase}).";
        }

        return $"{providerName} connection failed: {providerMessage}";
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

    private async Task<IReadOnlyList<PullRequestSummary>> GetPullRequestsForExplicitProjectAsync(
        ProviderConnectionSettings settings,
        string projectName,
        string? repositoryName,
        string? projectFilter,
        string? repositoryFilter,
        string? authorFilter,
        CancellationToken cancellationToken)
    {
        string checkedProjectName = RequireValue(projectName, "projectName");
        if (!MatchesOptionalFilter(checkedProjectName, projectFilter))
        {
            return Array.Empty<PullRequestSummary>();
        }

        List<PullRequestSummary> summaries = new List<PullRequestSummary>();
        if (!string.IsNullOrWhiteSpace(repositoryName))
        {
            string checkedRepositoryName = RequireValue(repositoryName, "repositoryName");
            if (!MatchesOptionalFilter(checkedRepositoryName, repositoryFilter))
            {
                return Array.Empty<PullRequestSummary>();
            }

            await this.AddPullRequestsForRepositoryAsync(settings, checkedProjectName, checkedRepositoryName, authorFilter, summaries, cancellationToken);
            return summaries;
        }

        IReadOnlyList<string> repositories = await this.GetRepositoryNamesAsync(settings, checkedProjectName, repositoryFilter, cancellationToken);
        foreach (string currentRepository in repositories)
        {
            _ = await this.TryAddPullRequestsForRepositoryAsync(settings, checkedProjectName, currentRepository, authorFilter, summaries, cancellationToken);
        }

        return summaries;
    }

    private async Task<IReadOnlyList<PullRequestSummary>> GetPullRequestsForFilteredProjectAsync(
        ProviderConnectionSettings settings,
        string projectFilter,
        string? repositoryName,
        string? repositoryFilter,
        string? authorFilter,
        CancellationToken cancellationToken)
    {
        string targetedProjectName = RequireValue(projectFilter, "projectFilter");
        IReadOnlyList<string> repositories = !string.IsNullOrWhiteSpace(repositoryName)
            ? new[] { RequireValue(repositoryName, "repositoryName") }
            : await this.GetRepositoryNamesAsync(settings, targetedProjectName, repositoryFilter, cancellationToken);

        List<PullRequestSummary> summaries = new List<PullRequestSummary>();
        foreach (string currentRepository in repositories)
        {
            await this.AddPullRequestsForRepositoryAsync(settings, targetedProjectName, currentRepository, authorFilter, summaries, cancellationToken);
        }

        return summaries;
    }

    private async Task<IReadOnlyList<PullRequestSummary>> GetPullRequestsAcrossProjectsAsync(
        ProviderConnectionSettings settings,
        string? repositoryName,
        string? repositoryFilter,
        string? authorFilter,
        CancellationToken cancellationToken)
    {
        List<PullRequestSummary> summaries = new List<PullRequestSummary>();
        IReadOnlyList<string> projects = await this.GetProjectNamesAsync(settings, projectFilter: null, cancellationToken);
        foreach (string currentProject in projects)
        {
            IReadOnlyList<string> repositories = !string.IsNullOrWhiteSpace(repositoryName)
                ? new[] { RequireValue(repositoryName, "repositoryName") }
                : await this.TryGetRepositoryNamesAsync(settings, currentProject, repositoryFilter, cancellationToken);

            foreach (string currentRepository in repositories)
            {
                _ = await this.TryAddPullRequestsForRepositoryAsync(settings, currentProject, currentRepository, authorFilter, summaries, cancellationToken);
            }
        }

        return summaries;
    }

    private async IAsyncEnumerable<IReadOnlyList<PullRequestSummary>> StreamPullRequestsForExplicitProjectAsync(
        ProviderConnectionSettings settings,
        string projectName,
        string? repositoryName,
        string? projectFilter,
        string? repositoryFilter,
        string? authorFilter,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string checkedProjectName = RequireValue(projectName, "projectName");
        if (!MatchesOptionalFilter(checkedProjectName, projectFilter))
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

            IReadOnlyList<PullRequestSummary> directBatch = await this.GetPullRequestBatchForRepositoryAsync(
                settings,
                checkedProjectName,
                checkedRepositoryName,
                authorFilter,
                cancellationToken);
            if (directBatch.Count > 0)
            {
                yield return directBatch;
            }

            yield break;
        }

        IReadOnlyList<string> repositories = await this.GetRepositoryNamesAsync(settings, checkedProjectName, repositoryFilter, cancellationToken);
        foreach (string currentRepository in repositories)
        {
            IReadOnlyList<PullRequestSummary>? batch = await this.TryGetPullRequestBatchForRepositoryAsync(
                settings,
                checkedProjectName,
                currentRepository,
                authorFilter,
                cancellationToken);
            if (batch is { Count: > 0 })
            {
                yield return batch;
            }
        }
    }

    private async IAsyncEnumerable<IReadOnlyList<PullRequestSummary>> StreamPullRequestsForFilteredProjectAsync(
        ProviderConnectionSettings settings,
        string projectFilter,
        string? repositoryName,
        string? repositoryFilter,
        string? authorFilter,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string targetedProjectName = RequireValue(projectFilter, "projectFilter");
        IReadOnlyList<string> repositories = !string.IsNullOrWhiteSpace(repositoryName)
            ? new[] { RequireValue(repositoryName, "repositoryName") }
            : await this.GetRepositoryNamesAsync(settings, targetedProjectName, repositoryFilter, cancellationToken);

        foreach (string currentRepository in repositories)
        {
            IReadOnlyList<PullRequestSummary> batch = await this.GetPullRequestBatchForRepositoryAsync(
                settings,
                targetedProjectName,
                currentRepository,
                authorFilter,
                cancellationToken);
            if (batch.Count > 0)
            {
                yield return batch;
            }
        }
    }

    private async IAsyncEnumerable<IReadOnlyList<PullRequestSummary>> StreamPullRequestsAcrossProjectsAsync(
        ProviderConnectionSettings settings,
        string? repositoryName,
        string? repositoryFilter,
        string? authorFilter,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IReadOnlyList<string> projects = await this.GetProjectNamesAsync(settings, projectFilter: null, cancellationToken);
        foreach (string currentProject in projects)
        {
            IReadOnlyList<string> repositories = !string.IsNullOrWhiteSpace(repositoryName)
                ? new[] { RequireValue(repositoryName, "repositoryName") }
                : await this.TryGetRepositoryNamesAsync(settings, currentProject, repositoryFilter, cancellationToken);

            foreach (string currentRepository in repositories)
            {
                IReadOnlyList<PullRequestSummary>? batch = await this.TryGetPullRequestBatchForRepositoryAsync(
                    settings,
                    currentProject,
                    currentRepository,
                    authorFilter,
                    cancellationToken);
                if (batch is { Count: > 0 })
                {
                    yield return batch;
                }
            }
        }
    }

    private async Task AddPullRequestsForRepositoryAsync(
        ProviderConnectionSettings settings,
        string projectName,
        string repositoryName,
        string? authorFilter,
        List<PullRequestSummary> summaries,
        CancellationToken cancellationToken)
    {
        summaries.AddRange(await this.GetPullRequestBatchForRepositoryAsync(settings, projectName, repositoryName, authorFilter, cancellationToken));
    }

    private async Task<IReadOnlyList<PullRequestSummary>> GetPullRequestBatchForRepositoryAsync(
        ProviderConnectionSettings settings,
        string projectName,
        string repositoryName,
        string? authorFilter,
        CancellationToken cancellationToken)
    {
        string requestUri = BuildPullRequestsEndpoint(settings, projectName, repositoryName);
        using HttpRequestMessage request = CreateRequest(HttpMethod.Get, requestUri, settings.PersonalAccessToken);
        using HttpResponseMessage response = await this._httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessStatusCodeAsync(response, "pull request retrieval", cancellationToken);

        await using Stream contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(contentStream, cancellationToken: cancellationToken);
        JsonElement pullRequests = GetArrayProperty(document.RootElement, VALUE_PROPERTY_NAME, "pull requests");

        List<PullRequestSummary> batch = new List<PullRequestSummary>();

        foreach (JsonElement pullRequest in pullRequests.EnumerateArray())
        {
            PullRequestSummary summary = new PullRequestSummary(
                GetStringValue(pullRequest, "pullRequestId"),
                GetStringValue(pullRequest, "title"),
                GetNestedStringValue(pullRequest, "createdBy", "displayName"),
                NormalizeBranchName(GetStringValue(pullRequest, "sourceRefName")),
                NormalizeBranchName(GetStringValue(pullRequest, "targetRefName")),
                GetStringValue(pullRequest, "status"),
                projectName,
                repositoryName,
                GetPullRequestUrl(pullRequest),
                GetDateTimeOffsetValue(pullRequest, "creationDate"));

            if (MatchesOptionalFilter(summary.Author, authorFilter))
            {
                batch.Add(summary);
            }
        }

        return batch;
    }

    private async Task<bool> TryAddPullRequestsForRepositoryAsync(
        ProviderConnectionSettings settings,
        string projectName,
        string repositoryName,
        string? authorFilter,
        List<PullRequestSummary> summaries,
        CancellationToken cancellationToken)
    {
        try
        {
            await this.AddPullRequestsForRepositoryAsync(settings, projectName, repositoryName, authorFilter, summaries, cancellationToken);
            return true;
        }
        catch (SourceControlRequestFailedException ex) when (IsSkippableEnumerationFailure(ex))
        {
            return false;
        }
    }

    private async Task<IReadOnlyList<PullRequestSummary>?> TryGetPullRequestBatchForRepositoryAsync(
        ProviderConnectionSettings settings,
        string projectName,
        string repositoryName,
        string? authorFilter,
        CancellationToken cancellationToken)
    {
        try
        {
            return await this.GetPullRequestBatchForRepositoryAsync(settings, projectName, repositoryName, authorFilter, cancellationToken);
        }
        catch (SourceControlRequestFailedException ex) when (IsSkippableEnumerationFailure(ex))
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<string>> GetProjectNamesAsync(
        ProviderConnectionSettings settings,
        string? projectFilter,
        CancellationToken cancellationToken)
    {
        string requestUri = BuildProjectsEndpoint(settings);
        using HttpRequestMessage request = CreateRequest(HttpMethod.Get, requestUri, settings.PersonalAccessToken);
        using HttpResponseMessage response = await this._httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessStatusCodeAsync(response, "project listing", cancellationToken);

        await using Stream contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(contentStream, cancellationToken: cancellationToken);
        JsonElement projects = GetArrayProperty(document.RootElement, VALUE_PROPERTY_NAME, "projects");

        List<string> projectNames = new List<string>();
        foreach (JsonElement project in projects.EnumerateArray())
        {
            string name = GetStringValue(project, "name");
            if (MatchesOptionalFilter(name, projectFilter))
            {
                projectNames.Add(name);
            }
        }

        return projectNames;
    }

    private async Task<IReadOnlyList<string>> GetRepositoryNamesAsync(
        ProviderConnectionSettings settings,
        string projectName,
        string? repositoryFilter,
        CancellationToken cancellationToken)
    {
        string requestUri = BuildRepositoriesEndpoint(settings, projectName);
        using HttpRequestMessage request = CreateRequest(HttpMethod.Get, requestUri, settings.PersonalAccessToken);
        using HttpResponseMessage response = await this._httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessStatusCodeAsync(response, "repository listing", cancellationToken);

        await using Stream contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(contentStream, cancellationToken: cancellationToken);
        JsonElement repositories = GetArrayProperty(document.RootElement, VALUE_PROPERTY_NAME, "repositories");

        List<string> repositoryNames = new List<string>();
        foreach (JsonElement repository in repositories.EnumerateArray())
        {
            string name = GetStringValue(repository, "name");
            if (MatchesOptionalFilter(name, repositoryFilter))
            {
                repositoryNames.Add(name);
            }
        }

        return repositoryNames;
    }
    private async Task<IReadOnlyList<string>> TryGetRepositoryNamesAsync(
        ProviderConnectionSettings settings,
        string projectName,
        string? repositoryFilter,
        CancellationToken cancellationToken)
    {
        try
        {
            return await this.GetRepositoryNamesAsync(settings, projectName, repositoryFilter, cancellationToken);
        }
        catch (SourceControlRequestFailedException ex) when (IsSkippableEnumerationFailure(ex))
        {
            return Array.Empty<string>();
        }
    }

    private async Task<int> GetLatestIterationIdAsync(
        ProviderConnectionSettings settings,
        string projectName,
        string repositoryName,
        string pullRequestId,
        CancellationToken cancellationToken)
    {
        string requestUri = BuildPullRequestIterationsEndpoint(settings, projectName, repositoryName, pullRequestId);
        using HttpRequestMessage request = CreateRequest(HttpMethod.Get, requestUri, settings.PersonalAccessToken);
        using HttpResponseMessage response = await this._httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessStatusCodeAsync(response, "pull request iteration retrieval", cancellationToken);

        await using Stream contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(contentStream, cancellationToken: cancellationToken);
        JsonElement iterations = GetArrayProperty(document.RootElement, VALUE_PROPERTY_NAME, "pull request iterations");

        int? latestIterationId = null;
        foreach (JsonElement iteration in iterations.EnumerateArray())
        {
            if (!iteration.TryGetProperty("id", out JsonElement id) || !id.TryGetInt32(out int currentIterationId))
            {
                throw new InvalidOperationException("Azure DevOps response did not include a valid pull request iteration id.");
            }

            latestIterationId = !latestIterationId.HasValue || currentIterationId > latestIterationId.Value
                ? currentIterationId
                : latestIterationId;
        }

        return latestIterationId
            ?? throw new InvalidOperationException("Azure DevOps response did not include any pull request iterations.");
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
            return $"Azure DevOps {operationName} failed because authentication was rejected. Verify the personal access token and required repository permissions.";
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return $"Azure DevOps {operationName} failed because the requested resource was not found.";
        }

        string? providerMessage = await ReadProviderMessageAsync(response, cancellationToken);
        if (string.IsNullOrWhiteSpace(providerMessage))
        {
            return $"Azure DevOps {operationName} failed with status code {(int)response.StatusCode} ({response.ReasonPhrase}).";
        }

        return $"Azure DevOps {operationName} failed: {providerMessage}";
    }

    private static bool IsSkippableEnumerationFailure(SourceControlRequestFailedException ex)
        => ex.StatusCode is System.Net.HttpStatusCode.Unauthorized
            or System.Net.HttpStatusCode.Forbidden
            or System.Net.HttpStatusCode.NotFound;

    private static Uri ValidateHttpsRequestUri(string requestUri)
    {
        if (!Uri.TryCreate(requestUri, UriKind.Absolute, out Uri? uri))
        {
            throw new InvalidOperationException("Azure DevOps request URLs must be absolute.");
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Azure DevOps Server URL must use HTTPS.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException("Azure DevOps Server URL cannot include embedded credentials.");
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
        sanitized = _sensitiveSchemePattern.Replace(sanitized, "$1 [REDACTED]");
        sanitized = _sensitiveHeaderPattern.Replace(sanitized, "$1=[REDACTED]");
        return sanitized.Length <= 240 ? sanitized : sanitized[..240];
    }
}
