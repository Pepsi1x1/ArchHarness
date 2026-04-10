using ArchHarness.App.Agents;
using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Tests.Core;

public sealed class OrchestrationAgentVerificationTests
{
    [Fact]
    public async Task BuildClarificationSpecAsync_ParsesVerificationCommands()
    {
        OrchestrationAgent agent = CreateAgent("""
            {
              "task": "Harden verification",
              "desiredOutcome": "Tests and build are executable",
              "inScope": ["src"],
              "outOfScope": [],
              "constraints": ["Keep changes minimal"],
              "assumptions": [],
              "acceptanceCriteria": ["API tests pass"],
              "likelyTouchpoints": ["src/ArchHarness.App"],
              "openQuestions": [],
              "decisionNotes": ["Use executable verification"],
              "verificationCommands": [
                {
                  "name": "Run API tests",
                  "command": "dotnet test tests/ArchHarness.App.Tests/ArchHarness.App.Tests.csproj --filter WebApiTests",
                  "evidenceType": "test",
                  "criterion": "API tests pass",
                  "required": true
                }
              ]
            }
            """);

        ClarificationSpec spec = await agent.BuildClarificationSpecAsync(
            new RunRequest("Improve verification", "C:\\workspace", "existing-folder", "auto", null, null, "dotnet test"),
            "C:\\workspace");

        VerificationCommand command = Assert.Single(spec.VerificationCommands!);
        Assert.Equal("Run API tests", command.Name);
        Assert.Equal("API tests pass", command.Criterion);
        Assert.Equal("test", command.EvidenceType);
    }

    [Fact]
    public async Task ValidateCompletionAsync_CustomCriterionWithoutEvidence_FailsClosed()
    {
        OrchestrationAgent agent = CreateAgent(string.Empty);
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
            => "gpt-5.4";

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