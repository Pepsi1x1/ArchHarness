using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text;
using ArchHarness.App.SourceControl;
using ArchHarness.App.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Tests.Core;

public sealed class GitHubOAuthDeviceFlowServiceTests
{
    /// <summary>
    /// StartAsync — PrunesExpiredFlows — KeepsCacheBoundToActiveEntries
    /// </summary>
    [Fact]
    public async Task StartAsync_PrunesExpiredFlowsBeforeAddingNewFlow()
    {
        Queue<HttpResponseMessage> responses = new Queue<HttpResponseMessage>(new[]
        {
            CreateJsonResponse("""
                {
                  "device_code": "expired-device-code",
                  "user_code": "ABCD-EFGH",
                  "verification_uri": "https://github.com/login/device",
                  "expires_in": 0,
                  "interval": 5
                }
                """),
            CreateJsonResponse("""
                {
                  "device_code": "active-device-code",
                  "user_code": "IJKL-MNOP",
                  "verification_uri": "https://github.com/login/device",
                  "expires_in": 600,
                  "interval": 5
                }
                """)
        });
        GitHubOAuthDeviceFlowService service = CreateService((_, _) => responses.Dequeue());

        await service.StartAsync(CancellationToken.None);
        Assert.Equal(1, GetFlowCount(service));

        await service.StartAsync(CancellationToken.None);

        Assert.Equal(1, GetFlowCount(service));
    }

    /// <summary>
    /// PollAsync — PrunesExpiredFlows — RemovesOtherExpiredEntries
    /// </summary>
    [Fact]
    public async Task PollAsync_PrunesOtherExpiredFlowsBeforeReturningPending()
    {
        GitHubOAuthDeviceFlowService service = CreateService((_, _) => throw new InvalidOperationException("HTTP should not be called while polling before the next interval."));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        AddFlow(service, "expired-flow", "expired-device-code", now.AddMinutes(-5), 5, now.AddMinutes(-4));
        AddFlow(service, "active-flow", "active-device-code", now.AddMinutes(5), 30, now.AddMinutes(1));

        GitHubOAuthDeviceFlowPollResult result = await service.PollAsync("active-flow", CancellationToken.None);

        Assert.Equal("pending", result.Status);
        Assert.Equal(1, GetFlowCount(service));
    }

    /// <summary>
    /// PollAsync — RequestedExpiredFlow — ReturnsExpiredInsteadOfNotFound
    /// </summary>
    [Fact]
    public async Task PollAsync_ReturnsExpiredWhenRequestedFlowHasExpired()
    {
        GitHubOAuthDeviceFlowService service = CreateService((_, _) => throw new InvalidOperationException("HTTP should not be called for expired flows."));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        AddFlow(service, "expired-flow", "expired-device-code", now.AddMinutes(-5), 5, now.AddMinutes(-4));
        AddFlow(service, "another-expired-flow", "another-expired-device-code", now.AddMinutes(-3), 5, now.AddMinutes(-2));

        GitHubOAuthDeviceFlowPollResult result = await service.PollAsync("expired-flow", CancellationToken.None);

        Assert.Equal("expired", result.Status);
        Assert.Equal(0, GetFlowCount(service));
    }

    private static GitHubOAuthDeviceFlowService CreateService(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responseFactory)
    {
        HttpClient httpClient = new HttpClient(new StubHttpMessageHandler(responseFactory));
        TestHttpClientFactory httpClientFactory = new TestHttpClientFactory(httpClient);
        IOptions<GitHubOAuthOptions> options = Options.Create(new GitHubOAuthOptions
        {
            ClientId = "client-id",
            Scopes = ["repo", "read:org"]
        });

        return new GitHubOAuthDeviceFlowService(httpClientFactory, options, NullLogger<GitHubOAuthDeviceFlowService>.Instance);
    }

    private static HttpResponseMessage CreateJsonResponse(string json)
        => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static int GetFlowCount(GitHubOAuthDeviceFlowService service)
    {
        object flows = GetFlows(service);
        PropertyInfo countProperty = flows.GetType().GetProperty("Count") ?? throw new InvalidOperationException("Could not find the device flow count property.");
        return (int)(countProperty.GetValue(flows) ?? 0);
    }

    private static void AddFlow(GitHubOAuthDeviceFlowService service, string flowId, string deviceCode, DateTimeOffset expiresAtUtc, int intervalSeconds, DateTimeOffset nextPollAtUtc)
    {
        object flow = CreatePendingFlow(flowId, deviceCode, expiresAtUtc, intervalSeconds, nextPollAtUtc);
        object flows = GetFlows(service);
        MethodInfo tryAddMethod = flows.GetType().GetMethod("TryAdd") ?? throw new InvalidOperationException("Could not find the device flow TryAdd method.");
        bool added = (bool)(tryAddMethod.Invoke(flows, [flowId, flow]) ?? false);
        Assert.True(added);
    }

    private static object GetFlows(GitHubOAuthDeviceFlowService service)
        => typeof(GitHubOAuthDeviceFlowService)
            .GetField("_flows", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(service)
            ?? throw new InvalidOperationException("Could not access the device flow cache.");

    private static object CreatePendingFlow(string flowId, string deviceCode, DateTimeOffset expiresAtUtc, int intervalSeconds, DateTimeOffset nextPollAtUtc)
    {
        Type pendingFlowType = typeof(GitHubOAuthDeviceFlowService).GetNestedType("PendingDeviceFlow", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not find the pending device flow type.");
        ConstructorInfo constructor = pendingFlowType.GetConstructor([typeof(string), typeof(string), typeof(DateTimeOffset), typeof(int), typeof(DateTimeOffset)])
            ?? throw new InvalidOperationException("Could not find the pending device flow constructor.");

        return constructor.Invoke([flowId, deviceCode, expiresAtUtc, intervalSeconds, nextPollAtUtc]);
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _httpClient;

        public TestHttpClientFactory(HttpClient httpClient)
        {
            this._httpClient = httpClient;
        }

        public HttpClient CreateClient(string name)
        {
            _ = name;
            return this._httpClient;
        }
    }
}
