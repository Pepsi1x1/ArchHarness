using ArchHarness.App.Agents;
using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using ArchHarness.App.Workspace;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Tests.Core;

public sealed class RunVerificationWorkflowTests
{
    [Fact]
    public async Task RunAsync_RetriesAfterRemediationAndCapturesAttempts()
    {
        string workspaceRoot = Path.Combine(Path.GetTempPath(), $"archharness-workflow-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspaceRoot);

        try
        {
            IWorkspaceAdapter adapter = WorkspaceAdapterFactory.Create("existing-folder", workspaceRoot);
            await adapter.InitializeAsync(null, initGit: false, CancellationToken.None);

            RuntimeStateAccessors accessors = new RuntimeStateAccessors(
                new PermissionHandlerModeAccessor(),
                new ReviewLoopAgentSelectionAccessor(),
                new AgentExecutionContextAccessor(),
                new WorkspaceRootAccessor());
            TouchingCopilotClient copilotClient = new TouchingCopilotClient(workspaceRoot);
            IOptions<AgentsOptions> options = Options.Create(new AgentsOptions());
            RunVerificationWorkflow workflow = new RunVerificationWorkflow(
                new FakeVerificationCommandRunner(),
                new FakeRunCompletionValidator(),
                new FrontendDeveloperAgent(copilotClient, new StubModelResolver(), new StubAgentToolPolicyProvider(), options),
                new BackendDeveloperAgent(copilotClient, new StubModelResolver(), new StubAgentToolPolicyProvider(), options),
                accessors);

            VerificationWorkflowResult result = await workflow.RunAsync(
                new RunVerificationRequest(
                    new RunRequest("Fix tests", workspaceRoot, "existing-folder", "auto", null, null, null),
                    new ExecutionPlan(
                        new[] { new ExecutionPlanStep(1, "BackendDeveloper", "Implement fix") },
                        new IterationStrategy(1, false),
                        new[] { "API tests pass" }),
                    new ArchitectureReview(Array.Empty<ArchitectureFinding>(), Array.Empty<string>()),
                    new SecurityReview(Array.Empty<SecurityFinding>(), Array.Empty<string>()),
                    new ClarificationSpec(
                        "Fix tests",
                        "API tests are green",
                        Array.Empty<string>(),
                        Array.Empty<string>(),
                        Array.Empty<string>(),
                        Array.Empty<string>(),
                        new[] { "API tests pass" },
                        Array.Empty<string>(),
                        Array.Empty<string>(),
                        Array.Empty<string>(),
                        new[] { new VerificationCommand("API tests", "dotnet test", "test", "API tests pass", true) }),
                    null,
                    Array.Empty<string>()),
                adapter,
                progress: null,
                CancellationToken.None);

            Assert.True(result.ValidationResult.Passed);
            Assert.Equal(2, result.ValidationResult.Attempts!.Count);
            Assert.Contains(result.FilesTouched, file => string.Equals(file, "verification-fix.txt", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    private sealed class FakeVerificationCommandRunner : IVerificationCommandRunner
    {
        private int _invocationCount;

        Task<IReadOnlyList<VerificationEvidence>> IVerificationCommandRunner.RunAsync(string workspaceRoot, IReadOnlyList<VerificationCommand> commands, IProgress<RuntimeProgressEvent>? progress, CancellationToken cancellationToken)
        {
            _ = workspaceRoot;
            _ = commands;
            _ = progress;
            _ = cancellationToken;
            _invocationCount++;
            bool passed = _invocationCount >= 2;
            return Task.FromResult<IReadOnlyList<VerificationEvidence>>(new[]
            {
                new VerificationEvidence("test", "API tests", passed, "dotnet test", passed ? 0 : 1, passed ? "API tests passed." : "API tests failed.", "API tests pass")
            });
        }
    }

    private sealed class FakeRunCompletionValidator : IRunCompletionValidator
    {
        public Task<CompletionValidationResult> ValidateAsync(
            CompletionValidationRequest request,
            CancellationToken cancellationToken)
        {
            VerificationEvidence evidence = Assert.Single(request.VerificationEvidence!);
            return Task.FromResult(new CompletionValidationResult(
                evidence.Passed,
                new[] { new CriterionResult("API tests pass", evidence.Passed, evidence.Summary) },
                evidence.Summary,
                "high",
                request.VerificationEvidence,
                new ImplementationAssessment(
                    evidence.Passed ? "PASS" : "FAIL",
                    evidence.Passed,
                    evidence.Summary,
                    new[] { evidence.Summary },
                    Array.Empty<string>(),
                    Array.Empty<string>())));
        }
    }

    private sealed class TouchingCopilotClient(string workspaceRoot) : ICopilotClient
    {
        public Task<string> CompleteAsync(string model, string prompt, CopilotCompletionOptions? options = null, string? agentId = null, string? agentRole = null, CancellationToken cancellationToken = default)
        {
            File.WriteAllText(Path.Combine(workspaceRoot, "verification-fix.txt"), prompt);
            return Task.FromResult("applied remediation");
        }

        public IReadOnlyList<CopilotModelUsage> GetUsageSnapshot()
            => Array.Empty<CopilotModelUsage>();
    }

    private sealed class StubModelResolver : IModelResolver
    {
        public IReadOnlyCollection<string> GetSupportedModels()
            => Array.Empty<string>();

        public string? ResolveReasoningEffort(string role)
            => null;

        public string Resolve(string role, IDictionary<string, string>? overrides)
            => WellKnownModelNames.GPT_5_4;

        public void ValidateConfiguredModelsOrThrow(IDictionary<string, string>? overrides = null)
        {
        }

        public void ValidateOrThrow(string model)
        {
        }
    }

    private sealed class StubAgentToolPolicyProvider : IAgentToolPolicyProvider
    {
        public AgentToolPolicy Resolve(string role)
            => new(Array.Empty<string>(), Array.Empty<string>());
    }
}
