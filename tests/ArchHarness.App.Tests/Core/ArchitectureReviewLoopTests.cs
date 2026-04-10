using System.Reflection;
using ArchHarness.App.Agents;
using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using ArchHarness.App.Storage;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Tests.Core;

public sealed class ArchitectureReviewLoopTests
{
    /// <summary>
    /// Verifies that writing a running architecture-loop checkpoint clears any stale failure message from a prior failed state.
    /// </summary>
    [Fact]
    public async Task WriteLoopCheckpointAsync_ClearsFailureMessageForRunningState()
    {
        FakeRunStateStore runStateStore = new FakeRunStateStore
        {
            ExistingState = new PersistedRunState(
                "run-001",
                "C:\\runs\\run-001",
                "C:\\workspace",
                RunStatuses.FAILED,
                RunPhases.ARCHITECTURE_LOOP,
                DateTimeOffset.UtcNow.AddMinutes(-5),
                DateTimeOffset.UtcNow.AddMinutes(-1),
                CreateRunRequest(),
                Array.Empty<int>(),
                1,
                string.Empty,
                Array.Empty<string>(),
                new ArchitectureReview(Array.Empty<ArchitectureFinding>(), Array.Empty<string>()),
                new SecurityReview(Array.Empty<SecurityFinding>(), Array.Empty<string>()),
                "previous failure")
        };

        RunContextAccessor runContextAccessor = new RunContextAccessor();
        runContextAccessor.SetCurrent(new RunContext("run-001", "C:\\runs\\run-001"));

        ArchitectureReviewLoop loop = new ArchitectureReviewLoop(
            CreateLoopAgentDependencies(),
            Options.Create(new AgentsOptions()),
            runStateStore,
            runContextAccessor,
            new RuntimeStateAccessors(
                new PermissionHandlerModeAccessor(),
                new ReviewLoopAgentSelectionAccessor(),
                new AgentExecutionContextAccessor(),
                new WorkspaceRootAccessor()));

        MethodInfo method = typeof(ArchitectureReviewLoop)
            .GetMethod("WriteLoopCheckpointAsync", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("WriteLoopCheckpointAsync was not found.");

        Task writeTask = (Task)(method.Invoke(loop, new object?[]
        {
            "C:\\workspace",
            new ArchitectureLoopRequest(
                new IterationStrategy(2, true),
                new ArchitectureReview(Array.Empty<ArchitectureFinding>(), Array.Empty<string>()),
                new SecurityReview(Array.Empty<SecurityFinding>(), Array.Empty<string>()),
                Array.Empty<string>(),
                null,
                null,
                CreateRunRequest()),
            2,
            new[] { "src/Updated.cs" },
            new ArchitectureReview(Array.Empty<ArchitectureFinding>(), Array.Empty<string>()),
            new SecurityReview(Array.Empty<SecurityFinding>(), Array.Empty<string>()),
            CancellationToken.None
        }) ?? throw new InvalidOperationException("WriteLoopCheckpointAsync did not return a task."));

        await writeTask;

        PersistedRunState writtenState = Assert.IsType<PersistedRunState>(runStateStore.LastWrittenState);
        Assert.Equal(RunStatuses.RUNNING, writtenState.Status);
        Assert.Null(writtenState.FailureMessage);
    }

    private static ArchitectureReviewLoop.LoopAgentDependencies CreateLoopAgentDependencies()
    {
        StubCopilotClient copilotClient = new StubCopilotClient();
        StubModelResolver modelResolver = new StubModelResolver();
        StubAgentToolPolicyProvider toolPolicyProvider = new StubAgentToolPolicyProvider();
        IOptions<AgentsOptions> agentsOptions = Options.Create(new AgentsOptions());

        return new ArchitectureReviewLoop.LoopAgentDependencies(
            new OrchestrationAgent(
                copilotClient,
                modelResolver,
                toolPolicyProvider,
                agentsOptions,
                new ReviewLoopAgentSelectionAccessor(),
                new StubExecutionPlanParser()),
            new CodingStyleAgent(copilotClient, modelResolver, toolPolicyProvider, agentsOptions),
            new SecurityAgent(copilotClient, modelResolver, toolPolicyProvider, agentsOptions),
            new ArchitectureAgent(copilotClient, modelResolver, toolPolicyProvider, agentsOptions));
    }

    private static RunRequest CreateRunRequest()
        => new(
            "Fix architecture issues",
            "C:\\workspace",
            "existing-folder",
            "auto",
            null,
            null,
            null);

    private sealed class FakeRunStateStore : IRunStateStore
    {
        public PersistedRunState? ExistingState { get; set; }

        public PersistedRunState? LastWrittenState { get; private set; }

        public PersistedRunState? GetState(string runDirectory)
            => this.ExistingState;

        public Task WriteStateAsync(string runDirectory, PersistedRunState state, CancellationToken cancellationToken)
        {
            this.LastWrittenState = state;
            return Task.CompletedTask;
        }
    }

    private sealed class StubCopilotClient : ICopilotClient
    {
        public Task<string> CompleteAsync(string model, string prompt, CopilotCompletionOptions? options = null, string? agentId = null, string? agentRole = null, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

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
            plan = new ExecutionPlan(Array.Empty<ExecutionPlanStep>(), new IterationStrategy(0, false), Array.Empty<string>());
            validationError = null;
            return true;
        }
    }
}
