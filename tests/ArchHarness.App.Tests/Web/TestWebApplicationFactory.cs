using System.Collections.Concurrent;
using System.Text.Json;
using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
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

    public string CreateWorkspace(string directoryName)
    {
        string path = Path.Combine(this._tempRoot, directoryName);
        Directory.CreateDirectory(path);
        return path;
    }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IProjectWorkspaceCatalog>();
            services.RemoveAll<IGlobalSettingsCatalog>();
            services.RemoveAll<IDiscoveredModelCatalog>();
            services.RemoveAll<IWebRunSessionManager>();

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
                copilotOptions));
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
}