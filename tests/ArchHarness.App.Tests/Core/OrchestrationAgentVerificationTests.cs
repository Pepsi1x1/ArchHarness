using ArchHarness.App.Agents;
using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Tests.Core;

public sealed class OrchestrationAgentVerificationTests
{
    [Fact]
    public async Task ValidateCompletionAsync_CustomCriterionWithoutEvidence_FailsClosed()
    {
        OrchestrationAgent agent = CreateAgent("""
                        {
                            "verdict": "FAIL",
                            "materiallyImplemented": true,
                            "summary": "Custom criterion has no executable proof.",
                            "evidence": ["Build passes"],
                            "gaps": [],
                            "risks": []
                        }
                        """);
        ExecutionPlan plan = new ExecutionPlan(
            new[] { new ExecutionPlanStep(1, "BackendDeveloper", "Implement feature") },
            new IterationStrategy(1, false),
            new[] { "Build passes" });
        ClarificationSpec spec = new ClarificationSpec(
            "Add verification",
            "Verification is executable",
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            new[] { "API tests pass" },
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<VerificationCommand>());

        CompletionValidationResult result = await agent.ValidateCompletionAsync(
            new CompletionValidationRequest(
                plan,
                new ArchitectureReview(Array.Empty<ArchitectureFinding>(), Array.Empty<string>()),
                new SecurityReview(Array.Empty<SecurityFinding>(), Array.Empty<string>()),
                null,
                spec,
                new BuildOutcome(true, "Build passed.", 1, DateTimeOffset.UtcNow),
                Array.Empty<VerificationEvidence>()));

        Assert.False(result.Passed);
        Assert.Contains(result.CriterionResults, criterion => criterion.Criterion == "API tests pass" && !criterion.Passed);
    }

    [Fact]
    public async Task ValidateCompletionAsync_VerifierCanFailMaterialImplementation_WhenCommandsPass()
    {
        OrchestrationAgent agent = CreateAgent("""
            {
              "verdict": "FAIL",
              "materiallyImplemented": false,
              "summary": "Build and tests passed, but the requested behavior is not materially present in the touched code.",
              "evidence": ["Build validation passed", "No relevant implementation files changed"],
              "gaps": ["Expected API endpoint changes are missing"],
              "risks": ["Prompt may have been satisfied only superficially"]
            }
            """);

        CompletionValidationResult result = await agent.ValidateCompletionAsync(
            new CompletionValidationRequest(
                new ExecutionPlan(
                    new[] { new ExecutionPlanStep(1, "BackendDeveloper", "Implement the requested API endpoint") },
                    new IterationStrategy(1, false),
                    new[] { "Build passes" }),
                new ArchitectureReview(Array.Empty<ArchitectureFinding>(), Array.Empty<string>()),
                new SecurityReview(Array.Empty<SecurityFinding>(), Array.Empty<string>()),
                null,
                new ClarificationSpec(
                    "Implement API endpoint",
                    "Endpoint exists and behaves as requested",
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    new[] { "Endpoint implemented" },
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    new[] { new VerificationCommand("Build validation", "dotnet build", "build", "Build passes", true) }),
                new BuildOutcome(true, "Build passed.", 1, DateTimeOffset.UtcNow),
                new[] { new VerificationEvidence("build", "Build validation", true, "dotnet build", 0, "Build validation passed", "Build passes") },
                new[] { "src/SomeUnrelatedFile.cs" }));

        Assert.False(result.Passed);
        Assert.NotNull(result.Assessment);
        Assert.False(result.Assessment!.MateriallyImplemented);
        Assert.Contains(result.CriterionResults, criterion => criterion.Criterion == "Plan materially implemented" && !criterion.Passed);
    }

    private static OrchestrationAgent CreateAgent(string response)
        => new(
            new StubCopilotClient(response),
            new StubModelResolver(),
            new StubAgentToolPolicyProvider(),
            Options.Create(new AgentsOptions()),
            new ReviewLoopAgentSelectionAccessor(),
            new StubExecutionPlanParser());

    private sealed class StubCopilotClient(string response) : ICopilotClient
    {
        public Task<string> CompleteAsync(string model, string prompt, CopilotCompletionOptions? options = null, string? agentId = null, string? agentRole = null, CancellationToken cancellationToken = default)
            => Task.FromResult(response);

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

    private sealed class StubExecutionPlanParser : IExecutionPlanParser
    {
        public bool TryBuildExecutionPlan(string raw, string workspaceRoot, out ExecutionPlan plan, out string? validationError)
        {
            plan = new ExecutionPlan(Array.Empty<ExecutionPlanStep>(), new IterationStrategy(1, false), new[] { "Build passes" });
            validationError = null;
            return true;
        }
    }
}
