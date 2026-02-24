using ArchHarness.App.Agents;
using ArchHarness.App.Storage;
using ArchHarness.App.Workspace;

namespace ArchHarness.App.Core;

/// <summary>
/// Executes plan steps in dependency order, dispatching each step to the appropriate agent.
/// </summary>
public sealed class AgentStepExecutor
{
    private const string ORCHESTRATOR_SOURCE = "orchestrator";
    private readonly FrontendAgent _frontendAgent;
    private readonly BuilderAgent _builderAgent;
    private readonly ArchitectureAgent _architectureAgent;
    private readonly IArtefactStore _artefactStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentStepExecutor"/> class.
    /// </summary>
    /// <param name="frontendAgent">Agent that creates frontend plans.</param>
    /// <param name="builderAgent">Agent that implements code changes.</param>
    /// <param name="architectureAgent">Agent that performs architecture reviews.</param>
    /// <param name="artefactStore">Store for persisting run events.</param>
    public AgentStepExecutor(
        FrontendAgent frontendAgent,
        BuilderAgent builderAgent,
        ArchitectureAgent architectureAgent,
        IArtefactStore artefactStore)
    {
        _frontendAgent = frontendAgent;
        _builderAgent = builderAgent;
        _architectureAgent = architectureAgent;
        _artefactStore = artefactStore;
    }

    /// <summary>
    /// Executes all steps in the execution plan in dependency order, dispatching each to the
    /// correct agent and tracking completion.
    /// </summary>
    /// <param name="plan">The execution plan containing steps to run.</param>
    /// <param name="adapter">Workspace adapter for file and diff operations.</param>
    /// <param name="request">The originating run request.</param>
    /// <param name="runId">Unique identifier for the current run.</param>
    /// <param name="runDirectory">Directory where run artefacts are stored.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Token to signal cancellation.</param>
    /// <returns>The aggregated results of all step executions.</returns>
    public async Task<StepExecutionResult> ExecuteAsync(
        ExecutionPlan plan,
        IWorkspaceAdapter adapter,
        RunRequest request,
        string runId,
        string runDirectory,
        IProgress<RuntimeProgressEvent>? progress,
        CancellationToken cancellationToken)
    {
        string frontendPlan = string.Empty;
        IReadOnlyList<string> filesTouched = Array.Empty<string>();
        ArchitectureReview review = new ArchitectureReview(Array.Empty<ArchitectureFinding>(), Array.Empty<string>());

        Dictionary<string, Func<ExecutionPlanStep, Task>> agentStrategies = new Dictionary<string, Func<ExecutionPlanStep, Task>>
        {
            ["Frontend"] = async (ExecutionPlanStep s) =>
            {
                frontendPlan = await _frontendAgent.CreatePlanAsync(
                    adapter,
                    s.Objective,
                    request.ModelOverrides,
                    _frontendAgent.Id,
                    _frontendAgent.Role,
                    cancellationToken);
            },
            ["Builder"] = async (ExecutionPlanStep s) =>
            {
                filesTouched = await _builderAgent.ImplementAsync(
                    adapter,
                    s.Objective,
                    request.ModelOverrides,
                    null,
                    _builderAgent.Id,
                    _builderAgent.Role,
                    cancellationToken);
            },
            ["Architecture"] = async (ExecutionPlanStep s) =>
            {
                var latestDiff = await adapter.DiffAsync(cancellationToken);
                review = await _architectureAgent.ReviewAsync(
                    new ArchitectureReviewRequest(
                        DelegatedPrompt: s.Objective,
                        Diff: latestDiff,
                        WorkspaceRoot: adapter.RootPath,
                        FilesTouched: filesTouched,
                        LanguageScope: s.Languages,
                        ModelOverrides: request.ModelOverrides),
                    _architectureAgent.Id,
                    _architectureAgent.Role,
                    cancellationToken);
            }
        };

        var pendingSteps = plan.Steps.ToDictionary(s => s.Id);
        var completedStepIds = new HashSet<int>();
        while (pendingSteps.Count > 0)
        {
            var step = pendingSteps.Values
                .OrderBy(s => s.Id)
                .FirstOrDefault(s => DependenciesSatisfied(s, completedStepIds, pendingSteps));

            if (step is null)
            {
                step = pendingSteps.Values.OrderBy(s => s.Id).First();
                await _artefactStore.AppendEventAsync(runDirectory, new
                {
                    runId,
                    source = ORCHESTRATOR_SOURCE,
                    message = $"Dependency deadlock detected; force-executing step {step.Id}."
                }, cancellationToken);
            }

            await _artefactStore.AppendEventAsync(runDirectory, new { runId, source = step.Agent, message = step.Objective }, cancellationToken);
            progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, step.Agent, "Delegated prompt started", step.Objective));
            if (!agentStrategies.TryGetValue(step.Agent, out Func<ExecutionPlanStep, Task>? strategy))
            {
                throw new InvalidOperationException($"Unrecognized agent role: '{step.Agent}'.");
            }

            await strategy(step);

            completedStepIds.Add(step.Id);
            pendingSteps.Remove(step.Id);
        }

        return new StepExecutionResult(frontendPlan, filesTouched, review);
    }

    private static bool DependenciesSatisfied(
        ExecutionPlanStep step,
        ISet<int> completedStepIds,
        IReadOnlyDictionary<int, ExecutionPlanStep> pendingSteps)
    {
        if (step.DependsOnStepIds is null || step.DependsOnStepIds.Count == 0)
        {
            return true;
        }

        foreach (var dep in step.DependsOnStepIds)
        {
            if (pendingSteps.ContainsKey(dep))
            {
                return false;
            }

            if (!completedStepIds.Contains(dep))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Contains the aggregated results from executing all plan steps.
    /// </summary>
    /// <param name="FrontendPlan">The frontend plan produced by the Frontend agent.</param>
    /// <param name="FilesTouched">Files modified by the Builder agent.</param>
    /// <param name="Review">The architecture review produced by the Architecture agent.</param>
    public sealed record StepExecutionResult(
        string FrontendPlan,
        IReadOnlyList<string> FilesTouched,
        ArchitectureReview Review);
}
