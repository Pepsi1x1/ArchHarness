using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArchHarness.App.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.SourceControl;

/// <summary>
/// Starts and polls GitHub OAuth device authorization flows for local clients.
/// </summary>
public sealed class GitHubOAuthDeviceFlowService : IGitHubOAuthDeviceFlowService
{
    private const string DeviceCodeEndpoint = "https://github.com/login/device/code";
    private const string AccessTokenEndpoint = "https://github.com/login/oauth/access_token";
    private const string UserEndpoint = "https://api.github.com/user";
    private const string DeviceGrantType = "urn:ietf:params:oauth:grant-type:device_code";
    private const string PendingStatus = "pending";
    private const string AuthorizedStatus = "authorized";
    private const string DeniedStatus = "denied";
    private const string ExpiredStatus = "expired";
    private const string ErrorStatus = "error";

    private readonly HttpClient _httpClient;
    private readonly ILogger<GitHubOAuthDeviceFlowService> _logger;
    private readonly GitHubOAuthOptions _options;
    private readonly ConcurrentDictionary<string, PendingDeviceFlow> _flows = new();

    public GitHubOAuthDeviceFlowService(HttpClient httpClient, IOptions<GitHubOAuthOptions> options, ILogger<GitHubOAuthDeviceFlowService> logger)
    {
        this._httpClient = httpClient;
        this._options = options.Value;
        this._logger = logger;
    }

    /// <inheritdoc />
    public bool IsEnabled => !string.IsNullOrWhiteSpace(this._options.ClientId);

    /// <inheritdoc />
    public async Task<GitHubOAuthDeviceFlowStartResult> StartAsync(CancellationToken cancellationToken)
    {
        string clientId = this.RequireClientId();
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, DeviceCodeEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string?>
            {
                ["client_id"] = clientId,
                ["scope"] = string.Join(' ', this._options.Scopes.Where(scope => !string.IsNullOrWhiteSpace(scope)))
            })
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("ArchHarness/1.0");

        using HttpResponseMessage response = await this._httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        GitHubDeviceCodeResponse payload = await DeserializeRequiredAsync<GitHubDeviceCodeResponse>(response, cancellationToken).ConfigureAwait(false);
        string flowId = Guid.NewGuid().ToString("N");
        PendingDeviceFlow flow = new PendingDeviceFlow(
            flowId,
            payload.DeviceCode,
            DateTimeOffset.UtcNow.AddSeconds(payload.ExpiresIn),
            Math.Max(1, payload.Interval),
            DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, payload.Interval)));
        this._flows[flowId] = flow;

        return new GitHubOAuthDeviceFlowStartResult(
            flowId,
            payload.UserCode,
            payload.VerificationUri,
            flow.ExpiresAtUtc,
            flow.IntervalSeconds);
    }

    /// <inheritdoc />
    public async Task<GitHubOAuthDeviceFlowPollResult> PollAsync(string flowId, CancellationToken cancellationToken)
    {
        if (!this._flows.TryGetValue(flowId, out PendingDeviceFlow? flow))
        {
            throw new KeyNotFoundException($"GitHub OAuth flow '{flowId}' was not found.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (now >= flow.ExpiresAtUtc)
        {
            this._flows.TryRemove(flowId, out _);
            return new GitHubOAuthDeviceFlowPollResult(ExpiredStatus, "The GitHub authorization code expired. Start the OAuth flow again.");
        }

        if (now < flow.NextPollAtUtc)
        {
            return PendingResult(flow, "Waiting for GitHub authorization to complete.");
        }

        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, AccessTokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string?>
            {
                ["client_id"] = this.RequireClientId(),
                ["device_code"] = flow.DeviceCode,
                ["grant_type"] = DeviceGrantType
            })
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("ArchHarness/1.0");

        using HttpResponseMessage response = await this._httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        GitHubAccessTokenResponse payload = await DeserializeRequiredAsync<GitHubAccessTokenResponse>(response, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(payload.AccessToken))
        {
            string login = await this.GetAuthenticatedLoginAsync(payload.AccessToken, cancellationToken).ConfigureAwait(false);
            this._flows.TryRemove(flowId, out _);
            return new GitHubOAuthDeviceFlowPollResult(
                AuthorizedStatus,
                $"Connected to GitHub as {login}.",
                payload.AccessToken,
                GitHubAuthenticationMode.OAuthDeviceFlow,
                login,
                payload.Scope,
                null,
                null);
        }

        if (string.Equals(payload.Error, "slow_down", StringComparison.OrdinalIgnoreCase))
        {
            PendingDeviceFlow slowedFlow = flow with
            {
                IntervalSeconds = payload.Interval.GetValueOrDefault(flow.IntervalSeconds + 5),
                NextPollAtUtc = now.AddSeconds(payload.Interval.GetValueOrDefault(flow.IntervalSeconds + 5))
            };
            this._flows[flowId] = slowedFlow;
            return PendingResult(slowedFlow, "GitHub requested slower polling while authorization is pending.");
        }

        if (string.Equals(payload.Error, "authorization_pending", StringComparison.OrdinalIgnoreCase))
        {
            PendingDeviceFlow pendingFlow = flow with { NextPollAtUtc = now.AddSeconds(flow.IntervalSeconds) };
            this._flows[flowId] = pendingFlow;
            return PendingResult(pendingFlow, "Waiting for GitHub authorization to complete.");
        }

        if (string.Equals(payload.Error, "expired_token", StringComparison.OrdinalIgnoreCase))
        {
            this._flows.TryRemove(flowId, out _);
            return new GitHubOAuthDeviceFlowPollResult(ExpiredStatus, "The GitHub authorization code expired. Start the OAuth flow again.");
        }

        if (string.Equals(payload.Error, "access_denied", StringComparison.OrdinalIgnoreCase))
        {
            this._flows.TryRemove(flowId, out _);
            return new GitHubOAuthDeviceFlowPollResult(DeniedStatus, "GitHub authorization was canceled.");
        }

        this._flows.TryRemove(flowId, out _);
        this._logger.LogWarning("GitHub OAuth device flow {FlowId} failed with error '{Error}'.", flowId, payload.Error);
        return new GitHubOAuthDeviceFlowPollResult(ErrorStatus, payload.ErrorDescription ?? payload.Error ?? "GitHub OAuth authorization failed.");
    }

    private async Task<string> GetAuthenticatedLoginAsync(string accessToken, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, UserEndpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.UserAgent.ParseAdd("ArchHarness/1.0");

        using HttpResponseMessage response = await this._httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        GitHubUserResponse payload = await DeserializeRequiredAsync<GitHubUserResponse>(response, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(payload.Login))
        {
            throw new InvalidOperationException("GitHub OAuth succeeded but the authenticated user login was missing.");
        }

        return payload.Login;
    }

    private string RequireClientId()
    {
        if (string.IsNullOrWhiteSpace(this._options.ClientId))
        {
            throw new InvalidOperationException("GitHub OAuth is not configured. Set gitHubOAuth.clientId before starting the device flow.");
        }

        return this._options.ClientId;
    }

    private static async Task<T> DeserializeRequiredAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
        where T : class
    {
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        T? payload = await JsonSerializer.DeserializeAsync<T>(stream, JsonDefaults.WEB_INDENTED, cancellationToken).ConfigureAwait(false);
        return payload ?? throw new InvalidOperationException("GitHub OAuth returned an empty response body.");
    }

    private static GitHubOAuthDeviceFlowPollResult PendingResult(PendingDeviceFlow flow, string message)
        => new GitHubOAuthDeviceFlowPollResult(
            PendingStatus,
            message,
            null,
            GitHubAuthenticationMode.None,
            null,
            null,
            flow.NextPollAtUtc,
            flow.IntervalSeconds);

    private sealed record PendingDeviceFlow(string FlowId, string DeviceCode, DateTimeOffset ExpiresAtUtc, int IntervalSeconds, DateTimeOffset NextPollAtUtc);

    private sealed class GitHubDeviceCodeResponse
    {
        [JsonPropertyName("device_code")]
        public string DeviceCode { get; init; } = string.Empty;

        [JsonPropertyName("user_code")]
        public string UserCode { get; init; } = string.Empty;

        [JsonPropertyName("verification_uri")]
        public string VerificationUri { get; init; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }

        [JsonPropertyName("interval")]
        public int Interval { get; init; }
    }

    private sealed class GitHubAccessTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("scope")]
        public string? Scope { get; init; }

        [JsonPropertyName("error")]
        public string? Error { get; init; }

        [JsonPropertyName("error_description")]
        public string? ErrorDescription { get; init; }

        [JsonPropertyName("interval")]
        public int? Interval { get; init; }
    }

    private sealed class GitHubUserResponse
    {
        [JsonPropertyName("login")]
        public string? Login { get; init; }
    }
}

/// <summary>
/// Starts and polls GitHub OAuth device authorization flows.
/// </summary>
public interface IGitHubOAuthDeviceFlowService
{
    /// <summary>
    /// Gets a value indicating whether the device flow is configured and available.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Starts a new device authorization flow.
    /// </summary>
    Task<GitHubOAuthDeviceFlowStartResult> StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Polls an existing device authorization flow.
    /// </summary>
    Task<GitHubOAuthDeviceFlowPollResult> PollAsync(string flowId, CancellationToken cancellationToken);
}

/// <summary>
/// Metadata returned when a GitHub OAuth device flow starts.
/// </summary>
public sealed record GitHubOAuthDeviceFlowStartResult(
    string FlowId,
    string UserCode,
    string VerificationUri,
    DateTimeOffset ExpiresAtUtc,
    int IntervalSeconds);

/// <summary>
/// Status returned while polling a GitHub OAuth device flow.
/// </summary>
public sealed record GitHubOAuthDeviceFlowPollResult(
    string Status,
    string Message,
    string? AccessToken = null,
    GitHubAuthenticationMode GitHubAuthenticationMode = GitHubAuthenticationMode.None,
    string? AuthenticatedUser = null,
    string? Scope = null,
    DateTimeOffset? NextPollAtUtc = null,
    int? IntervalSeconds = null);