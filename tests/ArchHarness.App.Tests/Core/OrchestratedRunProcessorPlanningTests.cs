using System.Reflection;
using ArchHarness.App.Agents;
using ArchHarness.App.Constants;
using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using ArchHarness.App.Storage;
using ArchHarness.App.Workspace;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Tests.Core;

public sealed class OrchestratedRunProcessorPlanningTests
{
    [Fact]
    public async Task RunClarificationLoopAsync_PlanningWorkflowUsesPlanningAgentModelAsync()
    {
        const string workspaceRoot = "C:\\workspace";
        const string runId = "run-1";
        const string runDirectory = "run-dir";

        RunRequest request = new(
            "Plan the change",
            workspaceRoot,
            "existing-folder",
            WorkflowNames.PLANNING,
            null,
            null,
            null);
        RecordingModelResolver modelResolver = new RecordingModelResolver();
        StubCopilotClient copilotClient = new StubCopilotClient();
        StubRunStateStore runStateStore = new StubRunStateStore();
        IOptions<AgentsOptions> agentsOptions = Options.Create(new AgentsOptions());
        StubAgentToolPolicyProvider toolPolicyProvider = new StubAgentToolPolicyProvider();
        ReviewLoopAgentSelectionAccessor reviewLoopAccessor = new ReviewLoopAgentSelectionAccessor();
        StubExecutionPlanParser executionPlanParser = new StubExecutionPlanParser();

        OrchestrationAgent orchestrationAgent = new OrchestrationAgent(
            copilotClient,
            modelResolver,
            toolPolicyProvider,
            agentsOptions,
            reviewLoopAccessor,
            executionPlanParser);
        PlanningAgent planningAgent = new PlanningAgent(
            copilotClient,
            modelResolver,
            toolPolicyProvider,
            agentsOptions,
            reviewLoopAccessor,
            executionPlanParser);

        OrchestratedRunProcessor processor = new OrchestratedRunProcessor(
            CreateServices(copilotClient, runStateStore),
            CreateStateAccessors(),
            new OrchestratorPlanningServices(orchestrationAgent, planningAgent, new StubRunVerificationWorkflow()),
            new WikiDocRunServices(new StubWikiDocWorkflow(), new WikiDocResumeStateBuilder(), new WikiDocRepositoryDiscoverer(), new WikiDocOutputResolver()),
            approvalBridge: null,
            userInputBridge: null);

        runStateStore.Seed(
            runDirectory,
            new PersistedRunState(
                runId,
                runDirectory,
                workspaceRoot,
                RunStatuses.RUNNING,
                RunPhases.PLANNING,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                request,
                Array.Empty<int>(),
                0,
                string.Empty,
                Array.Empty<string>(),
                new ArchitectureReview(Array.Empty<ArchitectureFinding>(), Array.Empty<string>()),
                new SecurityReview(Array.Empty<SecurityFinding>(), Array.Empty<string>())));

        MethodInfo method = typeof(OrchestratedRunProcessor).GetMethod("RunClarificationLoopAsync", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("RunClarificationLoopAsync was not found.");
        Type checkpointType = typeof(OrchestratedRunProcessor).GetNestedType("RunStateCheckpoint", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("RunStateCheckpoint was not found.");
        object checkpoint = Activator.CreateInstance(checkpointType, runId, runDirectory, workspaceRoot, request)
            ?? throw new InvalidOperationException("RunStateCheckpoint could not be created.");

        Task<(ClarificationSpec Spec, IReadOnlyList<ClarificationAnswer> Answers)> task =
            (Task<(ClarificationSpec Spec, IReadOnlyList<ClarificationAnswer> Answers)>)method.Invoke(
                processor,
                new object?[]
                {
                    checkpoint,
                    Array.Empty<ClarificationAnswer>(),
                    planningAgent,
                    null,
                    CancellationToken.None
                })!;

        _ = await task;

        Assert.Contains("planning", modelResolver.ResolvedRoles, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("orchestration", modelResolver.ResolvedRoles, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("planning-model", copilotClient.Models.Single());
    }

    [Fact]
    public async Task RunPlanningApprovalLoopAsync_RegenerateUsesCompactFollowUpPromptHistoryAsync()
    {
        string workspaceRoot = Path.Combine(Path.GetTempPath(), "archharness-planning-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        try
        {
            const string runId = "run-regenerate";
            string runDirectory = Path.Combine(workspaceRoot, ".agent-harness", "runs", runId);
            Directory.CreateDirectory(runDirectory);

            RunRequest request = new(
                "Plan the change",
                workspaceRoot,
                "existing-folder",
                WorkflowNames.PLANNING,
                null,
                null,
                null);
            StubRunStateStore runStateStore = new StubRunStateStore();
            RecordingPlanExecutor planExecutor = new RecordingPlanExecutor();
            StubRunEventLogger eventLogger = new StubRunEventLogger();
            WorkspaceRootAccessor workspaceRootAccessor = new WorkspaceRootAccessor();
            workspaceRootAccessor.SetCurrent(workspaceRoot);
            PlanningSessionRecorder recorder = new PlanningSessionRecorder(new InMemoryPlanningSessionStore());
            await recorder.EnsureAsync(workspaceRoot, runId, runId, CancellationToken.None);

            runStateStore.Seed(
                runDirectory,
                new PersistedRunState(
                    runId,
                    runDirectory,
                    workspaceRoot,
                    RunStatuses.RUNNING,
                    RunPhases.PLAN_APPROVAL,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    request,
                    Array.Empty<int>(),
                    0,
                    string.Empty,
                    Array.Empty<string>(),
                    new ArchitectureReview(Array.Empty<ArchitectureFinding>(), Array.Empty<string>()),
                    new SecurityReview(Array.Empty<SecurityFinding>(), Array.Empty<string>())));

            OrchestratedRunProcessor processor = new OrchestratedRunProcessor(
                CreateServices(new StubCopilotClient(), runStateStore, planExecutor, eventLogger),
                CreateStateAccessors(workspaceRootAccessor),
                CreatePlanningServices(),
                new WikiDocRunServices(new StubWikiDocWorkflow(), new WikiDocResumeStateBuilder(), new WikiDocRepositoryDiscoverer(), new WikiDocOutputResolver()),
                approvalBridge: new QueuePlanApprovalBridge(new[]
                {
                    new PlanApprovalResponse(PlanApprovalDecisions.REGENERATE, "Do not commit or push to git as part of the plan"),
                    new PlanApprovalResponse(PlanApprovalDecisions.APPROVED)
                }),
                userInputBridge: null,
                planningSessionRecorder: recorder);

            MethodInfo method = typeof(OrchestratedRunProcessor).GetMethod("RunPlanningApprovalLoopAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("RunPlanningApprovalLoopAsync was not found.");
            Type checkpointType = typeof(OrchestratedRunProcessor).GetNestedType("RunStateCheckpoint", BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("RunStateCheckpoint was not found.");
            object checkpoint = Activator.CreateInstance(checkpointType, runId, runDirectory, workspaceRoot, request)
                ?? throw new InvalidOperationException("RunStateCheckpoint could not be created.");
            ExecutionPlan plan = CreatePlan("Initial plan");
            ClarificationSpec spec = new(
                "Plan the change",
                "A usable plan",
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                new[] { "Build passes" },
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>());

            Task<ExecutionPlan> task = (Task<ExecutionPlan>)method.Invoke(
                processor,
                new object?[]
                {
                    checkpoint,
                    WorkspaceAdapterFactory.Create("existing-folder", workspaceRoot),
                    plan,
                    spec,
                    Array.Empty<ClarificationAnswer>(),
                    null,
                    CancellationToken.None
                })!;

            _ = await task;

            PlanningContext context = Assert.Single(planExecutor.Contexts);
            Assert.True(context.UseFollowUpOnlyPrompt);
            Assert.Null(context.PlanRevisionRequest);
            Assert.Collection(
                context.ConversationHistory!,
                message =>
                {
                    Assert.Equal(ConversationMessageKinds.PLAN_DECISION, message.Kind);
                    Assert.Equal("regenerate", message.Text);
                },
                message =>
                {
                    Assert.Equal(ConversationMessageKinds.PLAN_REVISION, message.Kind);
                    Assert.Equal("Do not commit or push to git as part of the plan", message.Text);
                });

            string[] planReviewAgentIds = eventLogger.Events
                .Where(evt => string.Equals(ReadStringProperty(evt, "kind"), "agent-delta", StringComparison.Ordinal)
                    && string.Equals(ReadStringProperty(evt, "title"), "Plan Review", StringComparison.Ordinal))
                .Select(evt => ReadStringProperty(evt, "agentId")!)
                .ToArray();
            Assert.Equal(2, planReviewAgentIds.Length);
            Assert.All(planReviewAgentIds, agentId => Assert.StartsWith($"planning-review-{runId}-", agentId, StringComparison.Ordinal));
            Assert.Equal(planReviewAgentIds.Length, planReviewAgentIds.Distinct(StringComparer.Ordinal).Count());
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    private static OrchestratorRunServices CreateServices(ICopilotClient copilotClient, IRunStateStore runStateStore, IPlanExecutor? planExecutor = null, IRunEventLogger? eventLogger = null)
    {
        RunInfrastructure infrastructure = new RunInfrastructure(
            new StubRunArtifactWriter(),
            eventLogger ?? new StubRunEventLogger(),
            new RunContextAccessor());

        RunSessionContext sessionContext = new RunSessionContext(
            Options.Create(new AgentsOptions()),
            copilotClient,
            runStateStore);

        return new OrchestratorRunServices(
            sessionContext,
            infrastructure,
            new OrchestratorRuntime.RunPhaseDependencies(new StubArchitectureReviewLoop(), planExecutor ?? new StubPlanExecutor()),
            new StubRunAgentModelUsageBuilder());
    }

    private static RuntimeStateAccessors CreateStateAccessors(WorkspaceRootAccessor? workspaceRootAccessor = null)
        => new RuntimeStateAccessors(
            new PermissionHandlerModeAccessor(),
            new ReviewLoopAgentSelectionAccessor(),
            new AgentExecutionContextAccessor(),
            workspaceRootAccessor ?? new WorkspaceRootAccessor());

    private static OrchestratorPlanningServices CreatePlanningServices()
    {
        IOptions<AgentsOptions> agentsOptions = Options.Create(new AgentsOptions());
        StubAgentToolPolicyProvider toolPolicyProvider = new StubAgentToolPolicyProvider();
        ReviewLoopAgentSelectionAccessor reviewLoopAccessor = new ReviewLoopAgentSelectionAccessor();
        StubExecutionPlanParser executionPlanParser = new StubExecutionPlanParser();
        StubCopilotClient copilotClient = new StubCopilotClient();
        RecordingModelResolver modelResolver = new RecordingModelResolver();
        return new OrchestratorPlanningServices(
            new OrchestrationAgent(copilotClient, modelResolver, toolPolicyProvider, agentsOptions, reviewLoopAccessor, executionPlanParser),
            new PlanningAgent(copilotClient, modelResolver, toolPolicyProvider, agentsOptions, reviewLoopAccessor, executionPlanParser),
            new StubRunVerificationWorkflow());
    }

    private static ExecutionPlan CreatePlan(string objective)
        => new(
            new[] { new ExecutionPlanStep(1, AgentNames.BACKEND_DEVELOPER, objective) },
            new IterationStrategy(1, false),
            new[] { "Build passes" });

    private static string? ReadStringProperty(object instance, string propertyName)
        => instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)?.GetValue(instance) as string;

    private sealed class RecordingModelResolver : IModelResolver
    {
        public List<string> ResolvedRoles { get; } = new List<string>();

        public IReadOnlyCollection<string> GetSupportedModels()
            => Array.Empty<string>();

        public string Resolve(string role, IDictionary<string, string>? overrides)
        {
            _ = overrides;
            this.ResolvedRoles.Add(role);
            return string.Equals(role, "planning", StringComparison.OrdinalIgnoreCase)
                ? "planning-model"
                : "orchestration-model";
        }

        public string? ResolveReasoningEffort(string role)
            => string.Equals(role, "planning", StringComparison.OrdinalIgnoreCase) ? "xhigh" : null;

        public void ValidateConfiguredModelsOrThrow(IDictionary<string, string>? overrides = null)
        {
            _ = overrides;
        }

        public void ValidateOrThrow(string model)
        {
            _ = model;
        }
    }

    private sealed class StubCopilotClient : ICopilotClient
    {
        public List<string> Models { get; } = new List<string>();

        public Task<string> CompleteAsync(string model, string prompt, CopilotCompletionOptions? options = null, string? agentId = null, string? agentRole = null, CancellationToken cancellationToken = default)
        {
            _ = prompt;
            _ = options;
            _ = agentId;
            _ = agentRole;
            _ = cancellationToken;
            this.Models.Add(model);
            return Task.FromResult("""
                {
                  "task": "Plan the change",
                  "desiredOutcome": "A usable plan",
                  "inScope": [],
                  "outOfScope": [],
                  "constraints": [],
                  "assumptions": [],
                  "acceptanceCriteria": ["Build passes"],
                  "likelyTouchpoints": [],
                  "openQuestions": [],
                  "decisionNotes": [],
                  "verificationCommands": []
                }
                """);
        }

        public IReadOnlyList<CopilotModelUsage> GetUsageSnapshot()
            => Array.Empty<CopilotModelUsage>();
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
            _ = raw;
            _ = workspaceRoot;
            plan = new ExecutionPlan(Array.Empty<ExecutionPlanStep>(), new IterationStrategy(1, false), new[] { "Build passes" });
            validationError = null;
            return true;
        }
    }

    private sealed class StubRunAgentModelUsageBuilder : IRunAgentModelUsageBuilder
    {
        public object[] Build(IDictionary<string, string>? overrides)
        {
            _ = overrides;
            return Array.Empty<object>();
        }
    }

    private sealed class StubRunVerificationWorkflow : IRunVerificationWorkflow
    {
        public Task<VerificationWorkflowResult> RunAsync(RunVerificationRequest request, IWorkspaceAdapter adapter, IProgress<RuntimeProgressEvent>? progress, CancellationToken cancellationToken)
            => Task.FromResult(new VerificationWorkflowResult(new CompletionValidationResult(true, Array.Empty<CriterionResult>()), Array.Empty<string>(), null));
    }

    private sealed class StubWikiDocWorkflow : IWikiDocWorkflow
    {
        public Task<WikiDocWorkflowResult> RegenerateAggregateAsync(RunRequest request, string runDirectory, IProgress<RuntimeProgressEvent>? progress, CancellationToken cancellationToken)
            => this.ExecuteAsync(request, runDirectory, resumeState: null, progress, cancellationToken);

        public Task<WikiDocWorkflowResult> ExecuteAsync(RunRequest request, string runDirectory, IProgress<RuntimeProgressEvent>? progress, CancellationToken cancellationToken)
            => this.ExecuteAsync(request, runDirectory, resumeState: null, progress, cancellationToken);

        public Task<WikiDocWorkflowResult> ExecuteAsync(RunRequest request, string runDirectory, WikiDocResumeState? resumeState, IProgress<RuntimeProgressEvent>? progress, CancellationToken cancellationToken)
        {
            _ = request;
            _ = runDirectory;
            _ = progress;
            _ = cancellationToken;
            return Task.FromResult(new WikiDocWorkflowResult(
                Array.Empty<string>(),
                new CompletionValidationResult(true, Array.Empty<CriterionResult>()),
                new WikiDocExecutionReport(
                    "C:\\workspace",
                    0,
                    Array.Empty<WikiDocRepositoryOutput>(),
                    new WikiDocAggregateOutput("C:\\workspace\\wiki", "C:\\workspace\\wiki\\MegaWiki.md", Array.Empty<string>(), false, null, null, null),
                    Array.Empty<WikiDocFallbackRecord>())));
        }
    }

    private sealed class StubRunArtifactWriter : IRunArtifactWriter
    {
        public string CreateRunDirectory(string workspaceRoot) => Path.Combine(workspaceRoot, ".agent-harness", "runs", "test-run");
        public Task WriteExecutionPlanAsync(string runDirectory, ExecutionPlan plan, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task WriteArchitectureReviewAsync(string runDirectory, ArchitectureReview review, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task WriteSecurityReviewAsync(string runDirectory, SecurityReview review, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task WriteBuildResultAsync(string runDirectory, object payload, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task WriteCompletionValidationAsync(string runDirectory, CompletionValidationResult validation, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task WriteFinalSummaryAsync(string runDirectory, string summary, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task WriteRunLogAsync(string runDirectory, object payload, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task WriteClarificationSpecAsync(string runDirectory, ClarificationSpec spec, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task WritePlanApprovalAsync(string runDirectory, PlanApproval approval, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubRunEventLogger : IRunEventLogger
    {
        public List<object> Events { get; } = new List<object>();

        public Task AppendEventAsync(string runDirectory, object eventData, CancellationToken cancellationToken)
        {
            _ = runDirectory;
            _ = cancellationToken;
            this.Events.Add(eventData);
            return Task.CompletedTask;
        }
        public Task PumpSessionEventsAsync(string runDirectory, string runId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PumpAgentEventsAsync(string runDirectory, string runId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CompleteRunAsync(string runDirectory, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubRunStateStore : IRunStateStore
    {
        private readonly Dictionary<string, PersistedRunState> _states = new(StringComparer.OrdinalIgnoreCase);

        public void Seed(string runDirectory, PersistedRunState state)
            => this._states[runDirectory] = state;

        public Task WriteStateAsync(string runDirectory, PersistedRunState state, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            this._states[runDirectory] = state;
            return Task.CompletedTask;
        }

        public PersistedRunState? GetState(string runDirectory)
            => this._states.TryGetValue(runDirectory, out PersistedRunState? state)
                ? state
                : null;
    }

    private sealed class StubArchitectureReviewLoop : IArchitectureReviewLoop
    {
        public Task<(ArchitectureReview Review, SecurityReview SecurityReview, IReadOnlyList<string> FilesTouched)> RunAsync(ArchitectureLoopRequest request, IWorkspaceAdapter adapter, IProgress<RuntimeProgressEvent>? progress, CancellationToken cancellationToken)
            => Task.FromResult((new ArchitectureReview(Array.Empty<ArchitectureFinding>(), Array.Empty<string>()), new SecurityReview(Array.Empty<SecurityFinding>(), Array.Empty<string>()), (IReadOnlyList<string>)Array.Empty<string>()));
    }

    private sealed class StubPlanExecutor : IPlanExecutor
    {
        public Task<ExecutionPlan> BuildPlanAsync(RunRequest request, IWorkspaceAdapter adapter, string runId, string runDirectory, PlanningContext? planningContext, CancellationToken cancellationToken)
            => Task.FromResult(new ExecutionPlan(Array.Empty<ExecutionPlanStep>(), new IterationStrategy(1, false), new[] { "Build passes" }));

        public Task<PlanExecutionResult> ExecuteApprovedPlanAsync(ExecutionPlan plan, RunRequest request, IWorkspaceAdapter adapter, StepExecutionContext context, IProgress<RuntimeProgressEvent>? progress, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<PlanExecutionResult> BuildAndExecuteAsync(RunRequest request, IWorkspaceAdapter adapter, string runId, string runDirectory, IProgress<RuntimeProgressEvent>? progress, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<PlanExecutionResult> ExecuteExistingPlanAsync(ExecutionPlan plan, RunRequest request, IWorkspaceAdapter adapter, PlanResumeContext context, IProgress<RuntimeProgressEvent>? progress, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class RecordingPlanExecutor : IPlanExecutor
    {
        public List<PlanningContext> Contexts { get; } = new List<PlanningContext>();

        public Task<ExecutionPlan> BuildPlanAsync(RunRequest request, IWorkspaceAdapter adapter, string runId, string runDirectory, PlanningContext? planningContext, CancellationToken cancellationToken)
        {
            if (planningContext is not null)
            {
                this.Contexts.Add(planningContext);
            }

            return Task.FromResult(CreatePlan("Regenerated plan"));
        }

        public Task<PlanExecutionResult> ExecuteApprovedPlanAsync(ExecutionPlan plan, RunRequest request, IWorkspaceAdapter adapter, StepExecutionContext context, IProgress<RuntimeProgressEvent>? progress, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<PlanExecutionResult> BuildAndExecuteAsync(RunRequest request, IWorkspaceAdapter adapter, string runId, string runDirectory, IProgress<RuntimeProgressEvent>? progress, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<PlanExecutionResult> ExecuteExistingPlanAsync(ExecutionPlan plan, RunRequest request, IWorkspaceAdapter adapter, PlanResumeContext context, IProgress<RuntimeProgressEvent>? progress, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class QueuePlanApprovalBridge : IPlanApprovalBridge
    {
        private readonly Queue<PlanApprovalResponse> _responses;

        public QueuePlanApprovalBridge(IEnumerable<PlanApprovalResponse> responses)
        {
            this._responses = new Queue<PlanApprovalResponse>(responses);
        }

        public Task<PlanApprovalResponse> RequestApprovalAsync(PlanApprovalRequest request, CancellationToken cancellationToken)
        {
            _ = request;
            _ = cancellationToken;
            return Task.FromResult(this._responses.Dequeue());
        }
    }

    private sealed class InMemoryPlanningSessionStore : IPlanningSessionStore
    {
        private readonly Dictionary<(string WorkspaceRoot, string SessionId), PlanningSession> _sessions = new();

        public PlanningSession? Get(string workspaceRoot, string sessionId)
            => this._sessions.TryGetValue((workspaceRoot, sessionId), out PlanningSession? session) ? session : null;

        public Task WriteAsync(string workspaceRoot, PlanningSession session, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            this._sessions[(workspaceRoot, session.Id)] = session;
            return Task.CompletedTask;
        }

        public Task<PlanningSession?> UpdateAsync(string workspaceRoot, string sessionId, Func<PlanningSession?, PlanningSession?> mutator, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            PlanningSession? existing = this.Get(workspaceRoot, sessionId);
            PlanningSession? updated = mutator(existing);
            if (updated is not null)
            {
                this._sessions[(workspaceRoot, sessionId)] = updated with { UpdatedAtUtc = DateTimeOffset.UtcNow };
            }

            return Task.FromResult(updated);
        }
    }
}
