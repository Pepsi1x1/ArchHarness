using System.Text.Json;
using ArchHarness.App.Agents;
using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Tests.Core;

public sealed class WikiDocWorkflowTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ArchHarnessWikiDocWorkflowTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExecuteAsync_DocumentsRepositoriesAndRecordsFallbacks()
    {
        string scanRoot = Path.Combine(this._root, "scan-root");
        string nestedRepo = Path.Combine(scanRoot, "services", "api");
        Directory.CreateDirectory(Path.Combine(scanRoot, ".git"));
        Directory.CreateDirectory(Path.Combine(scanRoot, "docs"));
        Directory.CreateDirectory(Path.Combine(nestedRepo, ".git"));
        Directory.CreateDirectory(Path.GetDirectoryName(nestedRepo)!);
        await File.WriteAllTextAsync(Path.Combine(scanRoot, "docs", "existing.md"), "# Existing docs");
        await File.WriteAllTextAsync(Path.Combine(nestedRepo, "wiki"), "reserved");
        string runDirectory = Path.Combine(scanRoot, ".agent-harness", "runs", "wikidoc-run");
        Directory.CreateDirectory(runDirectory);

        WikiDocWorkflow workflow = new WikiDocWorkflow(
            new WikiDocAgent(
                new StubCopilotClient(),
                new StubModelResolver(),
                new StubAgentToolPolicyProvider(),
                Options.Create(new AgentsOptions())),
            new RuntimeStateAccessors(
                new PermissionHandlerModeAccessor(),
                new ReviewLoopAgentSelectionAccessor(),
                new AgentExecutionContextAccessor(),
                new WorkspaceRootAccessor()),
            new WikiDocRepositoryDiscoverer(),
            new WikiDocOutputResolver(),
            new WikiDocMarkdownWriter());

        RunRequest request = new RunRequest(
            TaskPrompt: DefaultPrompts.WIKIDOC_TASK,
            WorkspacePath: scanRoot,
            WorkspaceMode: WorkspaceModes.EXISTING_FOLDER,
            Workflow: WorkflowNames.WIKIDOC,
            ProjectName: null,
            ModelOverrides: null,
            BuildCommand: null);

        WikiDocWorkflowResult result = await workflow.ExecuteAsync(request, runDirectory, progress: null, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(scanRoot, "wiki", "Home.md")));
        Assert.True(File.Exists(Path.Combine(scanRoot, "wiki", "MegaWiki.md")));
        Assert.True(Directory.Exists(Path.Combine(scanRoot, "wiki", "concepts")));
        Assert.DoesNotContain(
            result.Report.RepositoryOutputs,
            output => string.Equals(output.OutputRoot, Path.Combine(scanRoot, "docs"), StringComparison.OrdinalIgnoreCase));

        WikiDocRepositoryOutput nestedOutput = Assert.Single(result.Report.RepositoryOutputs, output => output.RepositoryRelativePath == "services/api");
        Assert.True(nestedOutput.UsedFallback);
        Assert.Contains(Path.Combine(runDirectory, "wikidoc-fallback", "services_api"), nestedOutput.OutputRoot, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(result.Report.Fallbacks, fallback => fallback.Scope == "repository");
        Assert.True(result.ValidationResult.Passed);

        string fallbackJson = await File.ReadAllTextAsync(Path.Combine(runDirectory, "WikiDocFallbacks.json"));
        JsonDocument fallbackDocument = JsonDocument.Parse(fallbackJson);
        Assert.NotEmpty(fallbackDocument.RootElement.EnumerateArray());
    }

    public void Dispose()
    {
        if (Directory.Exists(this._root))
        {
            foreach (string path in Directory.GetFileSystemEntries(this._root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(path, FileAttributes.Normal);
            }

            File.SetAttributes(this._root, FileAttributes.Normal);
            Directory.Delete(this._root, recursive: true);
        }
    }

    private sealed class StubCopilotClient : ICopilotClient
    {
        public Task<string> CompleteAsync(string model, string prompt, CopilotCompletionOptions? options = null, string? agentId = null, string? agentRole = null, CancellationToken cancellationToken = default)
        {
            _ = model;
            _ = prompt;
            _ = options;
            _ = agentRole;
            _ = cancellationToken;

            return agentId switch
            {
                "wikidoc-root" => Task.FromResult("""
                    {
                      "repositoryName": "scan-root",
                      "summary": "Top-level repository summary.",
                      "homeMarkdown": "# Scan Root\n\nTop-level repository documentation.",
                      "concepts": [{ "name": "run pipeline", "summary": "Shared execution pipeline." }]
                    }
                    """),
                "wikidoc-services_api" => Task.FromResult("""
                    {
                      "repositoryName": "services/api",
                      "summary": "Nested API repository summary.",
                      "homeMarkdown": "# API Repository\n\nNested repository documentation.",
                      "concepts": [{ "name": "run pipeline", "summary": "Nested repository also uses the pipeline." }]
                    }
                    """),
                "wikidoc-megawiki" => Task.FromResult("""
                    {
                      "megaWikiMarkdown": "# MegaWiki\n\nCombined repository overview.",
                      "conceptPages": [
                        {
                          "slug": "run-pipeline",
                          "title": "Run Pipeline",
                          "markdown": "# Run Pipeline\n\nShared concept page."
                        }
                      ]
                    }
                    """),
                _ => throw new InvalidOperationException($"Unexpected agent id: {agentId}")
            };
        }

        public IReadOnlyList<CopilotModelUsage> GetUsageSnapshot()
            => Array.Empty<CopilotModelUsage>();
    }

    private sealed class StubModelResolver : IModelResolver
    {
        public IReadOnlyCollection<string> GetSupportedModels()
            => new[] { "test-model" };

        public string Resolve(string role, IDictionary<string, string>? overrides)
        {
            _ = role;
            _ = overrides;
            return "test-model";
        }

        public string? ResolveReasoningEffort(string role)
        {
            _ = role;
            return null;
        }

        public void ValidateConfiguredModelsOrThrow(IDictionary<string, string>? overrides = null)
        {
            _ = overrides;
        }

        public void ValidateOrThrow(string model)
        {
            _ = model;
        }
    }

    private sealed class StubAgentToolPolicyProvider : IAgentToolPolicyProvider
    {
        public AgentToolPolicy Resolve(string role)
        {
            _ = role;
            return new AgentToolPolicy(Array.Empty<string>(), Array.Empty<string>());
        }
    }
}
