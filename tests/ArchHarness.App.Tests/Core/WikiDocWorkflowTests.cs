using System.Text.Json;
using ArchHarness.App.Agents;
using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using ArchHarness.App.Storage;
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
            new WikiDocMarkdownWriter(),
            new FileSystemGlobalSettingsCatalog(
                Path.Combine(this._root, "settings-0.json"),
                new AgentsOptions(),
                new CopilotOptions()));

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
        Assert.True(File.Exists(Path.Combine(scanRoot, "megawiki", "wiki", "Home.md")));
        Assert.DoesNotContain(
            result.Report.RepositoryOutputs,
            output => string.Equals(output.OutputRoot, Path.Combine(scanRoot, "docs"), StringComparison.OrdinalIgnoreCase));

        WikiDocRepositoryOutput nestedOutput = Assert.Single(result.Report.RepositoryOutputs, output => output.RepositoryRelativePath == "services/api");
        Assert.True(nestedOutput.UsedFallback);
        Assert.Contains(Path.Combine(runDirectory, "wikidoc-fallback", "services_api"), nestedOutput.OutputRoot, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(result.Report.Fallbacks, fallback => fallback.Scope == "repository");
        Assert.True(result.ValidationResult.Passed);
        Assert.Equal(Path.Combine(scanRoot, "megawiki", "wiki", "Home.md"), result.Report.AggregateOutput.MegaWikiPath);

        string megaWikiMarkdown = await File.ReadAllTextAsync(result.Report.AggregateOutput.MegaWikiPath);
        Assert.Contains("(../../wiki/Home.md)", megaWikiMarkdown, StringComparison.Ordinal);

        string fallbackJson = await File.ReadAllTextAsync(Path.Combine(runDirectory, "WikiDocFallbacks.json"));
        JsonDocument fallbackDocument = JsonDocument.Parse(fallbackJson);
        Assert.NotEmpty(fallbackDocument.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task ExecuteAsync_WritesVerificationEvidenceThatSupportsWikiDocCriteria()
    {
        string scanRoot = Path.Combine(this._root, "verification-root");
        string nestedRepo = Path.Combine(scanRoot, "services", "api");
        Directory.CreateDirectory(Path.Combine(scanRoot, ".git"));
        Directory.CreateDirectory(Path.Combine(scanRoot, "docs"));
        Directory.CreateDirectory(Path.Combine(nestedRepo, ".git"));
        Directory.CreateDirectory(Path.GetDirectoryName(nestedRepo)!);
        await File.WriteAllTextAsync(Path.Combine(scanRoot, "docs", "existing.md"), "# Existing docs");
        await File.WriteAllTextAsync(Path.Combine(nestedRepo, "wiki"), "reserved");
        string runDirectory = Path.Combine(scanRoot, ".agent-harness", "runs", "wikidoc-verification");
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
            new WikiDocMarkdownWriter(),
            new FileSystemGlobalSettingsCatalog(
                Path.Combine(this._root, "settings-2.json"),
                new AgentsOptions(),
                new CopilotOptions()));

        RunRequest request = new RunRequest(
            TaskPrompt: DefaultPrompts.WIKIDOC_TASK,
            WorkspacePath: scanRoot,
            WorkspaceMode: WorkspaceModes.EXISTING_FOLDER,
            Workflow: WorkflowNames.WIKIDOC,
            ProjectName: null,
            ModelOverrides: null,
            BuildCommand: null);

        await workflow.ExecuteAsync(request, runDirectory, progress: null, CancellationToken.None);

        CompletionValidationRequest validationRequest = new CompletionValidationRequest(
            new ExecutionPlan(Array.Empty<ExecutionPlanStep>(), new IterationStrategy(1, false), Array.Empty<string>()),
            new ArchitectureReview(Array.Empty<ArchitectureFinding>(), Array.Empty<string>()),
            new SecurityReview(Array.Empty<SecurityFinding>(), Array.Empty<string>()),
            null,
            null,
            null,
            Array.Empty<VerificationEvidence>(),
            Array.Empty<string>(),
            scanRoot,
            runDirectory,
            WorkflowNames.WIKIDOC);

        CriterionResult discoveryCriterion = CompletionCriteriaSupport.EvaluateCriterion(
            "Repository discovery processes each unique git repository under the scan root exactly once, includes the scan root when it is a git repository, and ignores non-git folders.",
            validationRequest,
            new ReviewLoopAgentSelection(false, false, false));
        CriterionResult megaWikiCriterion = CompletionCriteriaSupport.EvaluateCriterion(
            "<scan-root>\\megawiki\\wiki\\Home.md is generated and links to the per-repository wiki Home.md pages produced by the workflow.",
            validationRequest,
            new ReviewLoopAgentSelection(false, false, false));
        string[] criteria =
        {
            "Repository discovery processes each unique git repository under the scan root exactly once, includes the scan root when it is a git repository, and ignores non-git folders.",
            "The orchestrator creates and tracks exactly one isolated documentation session for each discovered repository.",
            "If a discovered repository contains an existing documentation folder that is safe to rename, the workflow renames or adopts it as `wiki` before writing output.",
            "For each writable discovered repository where repo-local `wiki` output is available, the workflow writes markdown under `wiki\\` with `Home.md` at the root and relative links suitable for Azure DevOps wiki publishing.",
            "For each writable discovered repository where repo-local `wiki` cannot be created or safely renamed into place, the workflow records and uses a deterministic explicit alternate output location rather than skipping the repository.",
            "<scan-root>\\megawiki\\wiki\\Home.md is generated and links to the per-repository wiki `Home.md` pages produced by the workflow.",
            "When the scan discovers multiple related repositories, the megawiki contains at least one generated cross-repository concept markdown page linked from the aggregate wiki.",
            "The existing web/Electron run experience can start the wiki-documentation workflow and emit progress through the active-run stream.",
            "The workflow writes only generated wiki artifacts under the selected per-repository wiki output locations and the scan-root megawiki path.",
            "Operator-facing documentation describes the `wikidoc` command, per-repository `wiki` output, megawiki output under `<scan-root>\\megawiki\\wiki\\`, and fallback output behavior."
        };

        Assert.True(discoveryCriterion.Passed, discoveryCriterion.Evidence);
        Assert.True(megaWikiCriterion.Passed, megaWikiCriterion.Evidence);
        foreach (string criterion in criteria)
        {
            CriterionResult result = CompletionCriteriaSupport.EvaluateCriterion(
                criterion,
                validationRequest,
                new ReviewLoopAgentSelection(false, false, false));
            Assert.True(result.Passed, $"{criterion}{Environment.NewLine}{result.Evidence}");
        }
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
                      "pages": ["Home.md"],
                      "concepts": [{ "name": "run pipeline", "summary": "Shared execution pipeline." }]
                    }
                    """),
                "wikidoc-services_api" => Task.FromResult("""
                    {
                      "repositoryName": "services/api",
                      "summary": "Nested API repository summary.",
                      "pages": ["Home.md"],
                      "concepts": [{ "name": "run pipeline", "summary": "Nested repository also uses the pipeline." }]
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
