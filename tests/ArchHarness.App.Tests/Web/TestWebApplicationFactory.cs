using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using ArchHarness.App;
using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using ArchHarness.App.SourceControl;
using ArchHarness.App.Storage;
using ArchHarness.App.Tests.TestHelpers;
using ArchHarness.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArchHarness.App.Tests.Web;

public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _tempRoot = TempWorkspaceHelper.CreateTempWorkspace();
    private readonly TestPersonalAccessTokenProtector _personalAccessTokenProtector = new TestPersonalAccessTokenProtector();
    private Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _azureDevOpsResponseFactory = static (_, _) => new HttpResponseMessage(HttpStatusCode.NotImplemented)
    {
        Content = new StringContent("Azure DevOps test response not configured.", Encoding.UTF8, "text/plain")
    };
    private Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _gitHubResponseFactory = static (_, _) => new HttpResponseMessage(HttpStatusCode.NotImplemented)
    {
        Content = new StringContent("GitHub test response not configured.", Encoding.UTF8, "text/plain")
    };
    private FakeGitHubOAuthDeviceFlowService _gitHubOAuthDeviceFlowService = new FakeGitHubOAuthDeviceFlowService();

    public string CreateWorkspace(string directoryName)
    {
        string path = Path.Combine(this._tempRoot, directoryName);
        Directory.CreateDirectory(path);
        return path;
    }

    public void ConfigureAzureDevOpsResponse(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responseFactory)
    {
        this._azureDevOpsResponseFactory = responseFactory;
    }

    public void ConfigureGitHubResponse(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responseFactory)
    {
        this._gitHubResponseFactory = responseFactory;
    }

    public void ConfigureGitHubOAuth(bool isEnabled)
    {
        this._gitHubOAuthDeviceFlowService = new FakeGitHubOAuthDeviceFlowService { IsEnabled = isEnabled };
    }

    public void ConfigureGitHubOAuthStartResult(GitHubOAuthDeviceFlowStartResult result)
    {
        this._gitHubOAuthDeviceFlowService.StartResult = result;
    }

    public void ConfigureGitHubOAuthPollResult(string flowId, GitHubOAuthDeviceFlowPollResult result)
    {
        this._gitHubOAuthDeviceFlowService.PollResults[flowId] = result;
    }

    public void SeedGlobalSettings(PersistedGlobalSettings settings)
    {
        File.WriteAllText(Path.Combine(this._tempRoot, "settings.json"), JsonSerializer.Serialize(settings, JsonDefaults.WEB_INDENTED));
    }

    public void SeedProviderConnections(params ProviderConnectionSettings[] providers)
    {
        FileSystemProviderConnectionCatalog catalog = new FileSystemProviderConnectionCatalog(
            Path.Combine(this._tempRoot, "providers.json"),
            this._personalAccessTokenProtector);

        foreach (ProviderConnectionSettings provider in providers)
        {
            catalog.SaveProvider(provider);
        }
    }

    public void SetSecureTokenStorageAvailable(bool canProtect)
    {
        this._personalAccessTokenProtector.CanProtectTokens = canProtect;
    }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IProjectWorkspaceCatalog>();
            services.RemoveAll<IGlobalSettingsCatalog>();
            services.RemoveAll<IProviderConnectionCatalog>();
            services.RemoveAll<IPersonalAccessTokenProtector>();
            services.RemoveAll<ISourceControlProviderService>();
            services.RemoveAll<IDiscoveredModelCatalog>();
            services.RemoveAll<IWebRunSessionManager>();
            services.RemoveAll<AzureDevOpsSourceControlService>();
            services.RemoveAll<GitHubSourceControlService>();
            services.RemoveAll<IGitHubOAuthDeviceFlowService>();
            services.RemoveAll<SourceControlProviderFactory>();

            AgentsOptions agentsOptions = new AgentsOptions
            {
                Orchestration = new AgentModelOptions { Model = "claude-sonnet-4.6" },
                FrontendDeveloper = new AgentModelOptions { Model = "claude-sonnet-4.6" },
                BackendDeveloper = new AgentModelOptions { Model = "gpt-5.3-codex" },
                Build = new AgentModelOptions { Model = "gpt-4.1" },
                CodingStyle = new AgentModelOptions { Model = "claude-opus-4.6" },
                Security = new AgentModelOptions { Model = "claude-opus-4.6" },
                Architecture = new AgentModelOptions { Model = "claude-opus-4.6", ArchitectureLoopMode = false }
            };
            CopilotOptions copilotOptions = new CopilotOptions { ConversationModel = "gpt-5-mini" };

            services.AddSingleton<IGlobalSettingsCatalog>(_ => new FileSystemGlobalSettingsCatalog(
                Path.Combine(this._tempRoot, "settings.json"),
                agentsOptions,
                copilotOptions,
                this._personalAccessTokenProtector));
            services.AddSingleton<IPersonalAccessTokenProtector>(this._personalAccessTokenProtector);
            services.AddSingleton<IProviderConnectionCatalog>(_ => new FileSystemProviderConnectionCatalog(
                Path.Combine(this._tempRoot, "providers.json"),
                this._personalAccessTokenProtector));
            services.AddSingleton<IProjectWorkspaceCatalog>(_ => new FileSystemProjectWorkspaceCatalog(
                Path.Combine(this._tempRoot, "projects.json")));
            services.AddSingleton<IDiscoveredModelCatalog>(_ =>
            {
                DiscoveredModelCatalog catalog = new DiscoveredModelCatalog();
                catalog.ReplaceModels(new[]
                {
                    new DiscoveredModel("gpt-5-mini", 0.25, "GPT-5 Mini"),
                    new DiscoveredModel("gpt-5.4", 1, "GPT-5.4"),
                    new DiscoveredModel("claude-sonnet-4.6", 1, "Claude Sonnet 4.6"),
                    new DiscoveredModel("claude-opus-4.6", 3, "Claude Opus 4.6"),
                    new DiscoveredModel("gpt-4.1", 1, "GPT-4.1"),
                    new DiscoveredModel("gpt-5.3-codex", 1, "GPT-5.3 Codex")
            });
                return catalog;
            });
            services.AddSingleton<IWebRunSessionManager, FakeWebRunSessionManager>();
            services.AddHttpClient<AzureDevOpsSourceControlService>()
                .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(this._azureDevOpsResponseFactory));
            services.AddHttpClient<GitHubSourceControlService>()
                .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(this._gitHubResponseFactory));
            services.AddSingleton<IGitHubOAuthDeviceFlowService>(this._gitHubOAuthDeviceFlowService);
            services.AddSingleton<SourceControlProviderFactory>();
            services.AddSingleton<ISourceControlProviderService, SourceControlProviderService>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        TempWorkspaceHelper.CleanupTempWorkspace(this._tempRoot);
    }

    private sealed class FakeWebRunSessionManager : IWebRunSessionManager
    {
        private readonly ConcurrentQueue<WebRunEvent> _events = new ConcurrentQueue<WebRunEvent>();
        private WebRunSnapshot _snapshot = new WebRunSnapshot(false, "idle", null, null, null, null, null, null, null);

        public Task<WebRunSnapshot> StartRunAsync(RunRequest request, CancellationToken cancellationToken)
        {
            string runId = "test-run-001";
            this._snapshot = new WebRunSnapshot(
                false,
                "accepted",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                runId,
                Path.Combine(request.WorkspacePath, ".agent-harness", "runs", runId),
                request.TaskPrompt,
                request.WorkspacePath,
                null);
            this._events.Enqueue(new WebRunEvent(DateTimeOffset.UtcNow, "run-state", "test", "accepted"));
            return Task.FromResult(this._snapshot);
        }

        Task<WebRunSnapshot> IWebRunSessionManager.ResumeRunAsync(PersistedRunState runState, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            this._snapshot = new WebRunSnapshot(
                true,
                "resuming",
                runState.StartedAtUtc,
                null,
                runState.RunId,
                runState.RunDirectory,
                runState.Request.TaskPrompt,
                runState.WorkspaceRoot,
                null);
            this._events.Enqueue(new WebRunEvent(DateTimeOffset.UtcNow, "run-state", "test", "resume-accepted"));
            return Task.FromResult(this._snapshot);
        }

        public WebRunSnapshot GetSnapshot() => this._snapshot;

        public Task<WebRunSnapshot> CancelRunAsync()
        {
            this._snapshot = this._snapshot with { IsRunning = false, Status = "canceled" };
            return Task.FromResult(this._snapshot);
        }

        public async IAsyncEnumerable<WebRunEvent> ReadEventsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            while (this._events.TryDequeue(out WebRunEvent? evt))
            {
                yield return evt;
            }

            await Task.CompletedTask;
        }
    }

    private sealed class TestPersonalAccessTokenProtector : IPersonalAccessTokenProtector
    {
        public bool CanProtectTokens { get; set; } = true;

        public bool CanProtect => this.CanProtectTokens;

        public string? UnavailableReason => this.CanProtectTokens
            ? null
            : "Secure token storage is not available in this test instance. Saving a personal access token requires a supported secure store.";

        public string Protect(string personalAccessToken, string? existingProtectedPersonalAccessToken = null)
        {
            if (!this.CanProtectTokens)
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

    private sealed class FakeGitHubOAuthDeviceFlowService : IGitHubOAuthDeviceFlowService
    {
        public bool IsEnabled { get; set; } = true;

        public GitHubOAuthDeviceFlowStartResult StartResult { get; set; } = new GitHubOAuthDeviceFlowStartResult(
            "flow-001",
            "ABCD-EFGH",
            "https://github.com/login/device",
            DateTimeOffset.UtcNow.AddMinutes(10),
            5);

        public Dictionary<string, GitHubOAuthDeviceFlowPollResult> PollResults { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<GitHubOAuthDeviceFlowStartResult> StartAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            return Task.FromResult(this.StartResult);
        }

        public Task<GitHubOAuthDeviceFlowPollResult> PollAsync(string flowId, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            if (!this.PollResults.TryGetValue(flowId, out GitHubOAuthDeviceFlowPollResult? result))
            {
                throw new KeyNotFoundException($"GitHub OAuth flow '{flowId}' was not found.");
            }

            return Task.FromResult(result);
        }
    }
}
