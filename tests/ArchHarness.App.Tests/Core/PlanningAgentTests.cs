using ArchHarness.App.Agents;
using ArchHarness.App.Constants;
using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Tests.Core;

public sealed class PlanningAgentTests
{
    [Fact]
    public void PlanningAgent_DoesNotInheritOrchestrationAgent()
    {
        Assert.False(typeof(OrchestrationAgent).IsAssignableFrom(typeof(PlanningAgent)));
        Assert.True(typeof(AgentBase).IsAssignableFrom(typeof(PlanningAgent)));
    }

    [Fact]
    public async Task BuildClarificationSpecAsync_UsesPlanningPromptGroupAsync()
    {
        RecordingCopilotClient copilotClient = new(new[]
        {
            """
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
              "verificationCommands": [
                {
                  "name": "Run API tests",
                  "command": "dotnet test tests/ArchHarness.App.Tests/ArchHarness.App.Tests.csproj --filter WebApiTests",
                  "evidenceType": "test",
                  "criterion": "Build passes",
                  "required": true
                }
              ]
            }
            """
        });
        PlanningAgent agent = CreateAgent(copilotClient);

        ClarificationSpec spec = await agent.BuildClarificationSpecAsync(
            CreatePlanningRequest(),
            "C:\\workspace",
            cancellationToken: CancellationToken.None);

        Assert.Equal("Plan the change", spec.Task);
        VerificationCommand command = Assert.Single(spec.VerificationCommands!);
        Assert.Equal("Run API tests", command.Name);
        Assert.Equal("Build passes", command.Criterion);
        Assert.Equal("test", command.EvidenceType);
        Assert.Contains("You are the planner", copilotClient.Prompts.Single(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("orchestration planner", copilotClient.Prompts.Single(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("You are the planner", copilotClient.Options.Single().SystemMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Planning flow:", ReadOrchestrationSystemPrompt(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Plan review must stay", ReadOrchestrationSystemPrompt(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildExecutionPlanAsync_UsesPlanningPromptGroupAndPlanningRoleAsync()
    {
        RecordingCopilotClient copilotClient = new(new[]
        {
            """
            {
              "steps": [{"id":1,"agent":"BackendDeveloper","objective":"Implement the change","parallelGroup":1}],
              "iterationStrategy": {"maxIterations": 2, "reviewRequired": true},
              "completionCriteria": ["Build passes"]
            }
            """
        });
        RecordingModelResolver modelResolver = new();
        PlanningAgent agent = CreateAgent(copilotClient, modelResolver);

        ExecutionPlan plan = await agent.BuildExecutionPlanAsync(
            CreatePlanningRequest(),
            "C:\\workspace",
            cancellationToken: CancellationToken.None);

        Assert.Contains(plan.Steps, step => step.Agent == AgentNames.BACKEND_DEVELOPER && step.Objective == "Implement the change");
        Assert.Equal(new[] { "planning" }, modelResolver.ResolvedRoles);
        Assert.Contains("You are the planner", copilotClient.Prompts.Single(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("orchestration planner", copilotClient.Prompts.Single(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("initial implementation plan", copilotClient.Prompts.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildExecutionPlanAsync_RetrySendsOnlyValidationErrorFollowUpAsync()
    {
        RecordingCopilotClient copilotClient = new(new[]
        {
            """
            {
              "steps": [
                                {"id":1,"agent":"BackendDeveloper","parallelGroup":1}
              ],
              "iterationStrategy": {"maxIterations": 2, "reviewRequired": true},
              "completionCriteria": ["Build passes"]
            }
            """,
            """
            {
              "steps": [{"id":1,"agent":"BackendDeveloper","objective":"Implement the change","parallelGroup":1}],
              "iterationStrategy": {"maxIterations": 2, "reviewRequired": true},
              "completionCriteria": ["Build passes"]
            }
            """
        });
        PlanningAgent agent = CreateAgent(copilotClient);

        ExecutionPlan plan = await agent.BuildExecutionPlanAsync(
            CreatePlanningRequest(),
            "C:\\workspace",
            cancellationToken: CancellationToken.None);

        Assert.Contains(plan.Steps, step => step.Agent == AgentNames.BACKEND_DEVELOPER && step.Objective == "Implement the change");
        Assert.Equal(2, copilotClient.Prompts.Count);
        Assert.Equal("Validation error: Step 0: missing or empty 'objective' field.", copilotClient.Prompts[1]);
        Assert.DoesNotContain("Previous response", copilotClient.Prompts[1], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("initial implementation plan", copilotClient.Prompts[1], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildExecutionPlanAsync_FollowUpOnlyPromptSendsCompactPlanningLedgerAsync()
    {
        RecordingCopilotClient copilotClient = new(new[]
        {
            """
            {
              "steps": [{"id":1,"agent":"BackendDeveloper","objective":"Revise the plan","parallelGroup":1}],
              "iterationStrategy": {"maxIterations": 2, "reviewRequired": true},
              "completionCriteria": ["Build passes"]
            }
            """
        });
        PlanningAgent agent = CreateAgent(copilotClient);
        PlanningContext planningContext = new(
            Spec: null,
            ClarificationAnswers: null,
            ConversationHistory: new[]
            {
                CreateConversationMessage(ConversationMessageKinds.PLAN_DECISION, "regenerate"),
                CreateConversationMessage(ConversationMessageKinds.PLAN_REVISION, "Do not commit or push to git as part of the plan")
            },
            UseFollowUpOnlyPrompt: true);

        _ = await agent.BuildExecutionPlanAsync(
            CreatePlanningRequest(),
            "C:\\workspace",
            planningContext,
            cancellationToken: CancellationToken.None);

        Assert.Equal(
            $"[plan-decision] user: regenerate{Environment.NewLine}[plan-revision] user: Do not commit or push to git as part of the plan",
            copilotClient.Prompts.Single());
        Assert.DoesNotContain("TaskPrompt:", copilotClient.Prompts.Single(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PlanRevisionRequest:", copilotClient.Prompts.Single(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("You are the planner", copilotClient.Options.Single().SystemMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static PlanningAgent CreateAgent(RecordingCopilotClient copilotClient, RecordingModelResolver? modelResolver = null)
        => new(
            copilotClient,
            modelResolver ?? new RecordingModelResolver(),
            new StubAgentToolPolicyProvider(),
            Options.Create(new AgentsOptions()),
            new ReviewLoopAgentSelectionAccessor(),
            new ExecutionPlanParser(new StubWorkspaceContextAnalyzer(), Options.Create(new AgentsOptions())));

    private static RunRequest CreatePlanningRequest()
        => new(
            "Plan the change",
            "C:\\workspace",
            "existing-folder",
            WorkflowNames.PLANNING,
            null,
            null,
            null);

    private static string ReadOrchestrationSystemPrompt()
        => File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Prompts",
            "Orchestration",
            "system.md"));

    private static ConversationMessage CreateConversationMessage(string kind, string text)
        => new(
            Guid.NewGuid().ToString("N"),
            ConversationRoles.USER,
            kind,
            text,
            Array.Empty<PromptAttachment>(),
            DateTimeOffset.UtcNow);

    private sealed class RecordingCopilotClient : ICopilotClient
    {
        private readonly Queue<string> _responses;

        public RecordingCopilotClient(IEnumerable<string> responses)
        {
            this._responses = new Queue<string>(responses);
        }

        public List<string> Prompts { get; } = new();

        public List<CopilotCompletionOptions> Options { get; } = new();

        public Task<string> CompleteAsync(string model, string prompt, CopilotCompletionOptions? options = null, string? agentId = null, string? agentRole = null, CancellationToken cancellationToken = default)
        {
            _ = model;
            _ = agentId;
            _ = agentRole;
            _ = cancellationToken;
            this.Prompts.Add(prompt);
            this.Options.Add(options ?? new CopilotCompletionOptions());
            return Task.FromResult(this._responses.Dequeue());
        }

        public IReadOnlyList<CopilotModelUsage> GetUsageSnapshot()
            => Array.Empty<CopilotModelUsage>();
    }

    private sealed class RecordingModelResolver : IModelResolver
    {
        public List<string> ResolvedRoles { get; } = new();

        public IReadOnlyCollection<string> GetSupportedModels()
            => Array.Empty<string>();

        public string Resolve(string role, IDictionary<string, string>? overrides)
        {
            _ = overrides;
            this.ResolvedRoles.Add(role);
            return role + "-model";
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

    private sealed class StubWorkspaceContextAnalyzer : IWorkspaceContextAnalyzer
    {
        public IReadOnlyList<string> DetectWorkspaceLanguages(string workspaceRoot)
        {
            _ = workspaceRoot;
            return new[] { "dotnet" };
        }

        public string EnforceWorkspaceRootInObjective(string objective, string workspaceRoot)
        {
            _ = workspaceRoot;
            return objective;
        }

        public bool IsReviewObjective(string objective)
            => objective.Contains("review", StringComparison.OrdinalIgnoreCase);
    }
}
