using ArchHarness.App.Constants;
using ArchHarness.App.Storage;
using ArchHarness.App.Workspace;

namespace ArchHarness.App.Core;

/// <summary>
/// Executes plan steps in dependency order, delegating step-specific work to focused collaborators.
/// </summary>
public sealed class AgentStepExecutor : IAgentStepExecutor
{
    private readonly IAgentStepDispatcher _stepDispatcher;
    private readonly IStepExecutionStateStore _stateStore;
    private readonly IArtefactStore _artefactStore;
    private readonly RuntimeStateAccessors _stateAccessors;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentStepExecutor"/> class.
    /// </summary>
    public AgentStepExecutor(
        IAgentStepDispatcher stepDispatcher,
        IStepExecutionStateStore stateStore,
        IArtefactStore artefactStore,
        RuntimeStateAccessors stateAccessors)
    {
        this._stepDispatcher = stepDispatcher;
        this._stateStore = stateStore;
        this._artefactStore = artefactStore;
        this._stateAccessors = stateAccessors;
    }

    /// <inheritdoc />
    public async Task<StepExecutionResult> ExecuteAsync(
        ExecutionPlan plan,
        IWorkspaceAdapter adapter,
        RunRequest request,
        StepExecutionContext context,
        IProgress<RuntimeProgressEvent>? progress,
        CancellationToken cancellationToken)
    {
        ExecutionState state = this._stateStore.CreateExecutionState(context.ResumeState);
        Dictionary<int, ExecutionPlanStep> pendingSteps = plan.Steps
            .Where(step => !(context.ResumeState?.CompletedStepIds.Contains(step.Id) ?? false))
            .ToDictionary(step => step.Id);
        HashSet<int> completedStepIds = new(context.ResumeState?.CompletedStepIds ?? Array.Empty<int>());

        await this._stateStore.WriteRunStateAsync(
            adapter.RootPath,
            request,
            context.RunId,
            context.RunDirectory,
            context.ResumeState?.ReviewIteration ?? 0,
            RunPhases.EXECUTING_PLAN,
            completedStepIds,
            state,
            null,
            cancellationToken).ConfigureAwait(false);

        while (pendingSteps.Count > 0)
        {
            List<ExecutionPlanStep> batch = ResolveDependencyReadyBatch(pendingSteps, completedStepIds);
            if (batch.Count == 0)
            {
                ExecutionPlanStep fallbackStep = pendingSteps.Values.OrderBy(candidate => candidate.Id).First();
                await this._artefactStore.AppendEventAsync(context.RunDirectory, new
                {
                    runId = context.RunId,
                    source = WellKnownSources.ORCHESTRATOR,
                    message = $"Dependency deadlock detected; force-executing step {fallbackStep.Id}."
                }, cancellationToken).ConfigureAwait(false);
                batch = new List<ExecutionPlanStep> { fallbackStep };
            }

            IReadOnlyList<StepOutcome> batchOutcomes;
            if (batch.Count == 1)
            {
                StepOutcome outcome = await this.ExecuteStepAsync(batch[0], adapter, request, context, state, progress, cancellationToken).ConfigureAwait(false);
                batchOutcomes = new[] { outcome };
            }
            else
            {
                Task<StepOutcome>[] batchTasks = batch.Select(step =>
                    this.ExecuteStepAsync(step, adapter, request, context, state, progress, cancellationToken))
                    .ToArray();
                batchOutcomes = await Task.WhenAll(batchTasks).ConfigureAwait(false);
            }

            foreach (StepOutcome outcome in batchOutcomes)
            {
                MergeOutcome(state, outcome);
                if (outcome.BuildOutcome is not null)
                {
                    await this._artefactStore.WriteBuildResultAsync(context.RunDirectory, outcome.BuildOutcome, cancellationToken).ConfigureAwait(false);
                }

                completedStepIds.Add(outcome.StepId);
                pendingSteps.Remove(outcome.StepId);
                await this._artefactStore.AppendEventAsync(context.RunDirectory, new
                {
                    runId = context.RunId,
                    source = outcome.Agent,
                    status = RunEventStatuses.COMPLETED,
                    stepId = outcome.StepId,
                    message = $"Step {outcome.StepId} completed"
                }, cancellationToken).ConfigureAwait(false);
            }

            await this._stateStore.WriteRunStateAsync(
                adapter.RootPath,
                request,
                context.RunId,
                context.RunDirectory,
                context.ResumeState?.ReviewIteration ?? 0,
                RunPhases.EXECUTING_PLAN,
                completedStepIds,
                state,
                null,
                cancellationToken).ConfigureAwait(false);
        }

        return new StepExecutionResult(state.FrontendPlan, state.FilesTouched, state.Review, state.SecurityReview, state.LastBuildOutcome);
    }

    internal static string BuildDelegatedPrompt(string objective, RunRequest request)
        => request.ArchitectureLoopMode
            ? ArchitectureLoopHelpers.BuildArchitectureLoopPrompt(objective, request.ArchitectureLoopPrompt)
            : objective;

    internal static IReadOnlyList<string> ResolveReviewFiles(
        IWorkspaceAdapter adapter,
        RunRequest request,
        IReadOnlyList<string> filesTouched,
        IReadOnlyList<string>? languageScope)
        => request.ArchitectureLoopMode
            ? ArchitectureLoopHelpers.EnumerateWorkspaceFiles(adapter.RootPath, languageScope)
            : filesTouched;

    internal static List<ExecutionPlanStep> ResolveDependencyReadyBatch(
        IReadOnlyDictionary<int, ExecutionPlanStep> pendingSteps,
        ISet<int> completedStepIds)
    {
        return pendingSteps.Values
            .Where(candidate => DependenciesSatisfied(candidate, completedStepIds, pendingSteps))
            .OrderBy(candidate => candidate.Id)
            .ToList();
    }

    private async Task<StepOutcome> ExecuteStepAsync(
        ExecutionPlanStep step,
        IWorkspaceAdapter adapter,
        RunRequest request,
        StepExecutionContext context,
        ExecutionState state,
        IProgress<RuntimeProgressEvent>? progress,
        CancellationToken cancellationToken)
    {
        await this._artefactStore.AppendEventAsync(context.RunDirectory, new { runId = context.RunId, source = step.Agent, message = step.Objective }, cancellationToken).ConfigureAwait(false);
        progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, step.Agent, "Delegated prompt started", step.Objective));

        AgentExecutionContext? previousAgentContext = this._stateAccessors.AgentExecutionContext.Current;
        this._stateAccessors.AgentExecutionContext.SetCurrent(this._stepDispatcher.ResolveAgentExecutionContext(step.Agent));
        try
        {
            return await this._stepDispatcher.ExecuteAsync(step, adapter, request, state.FilesTouched, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (StructuredOutputParser.IsParseFailure(ex))
        {
            await this.AppendStepFailureAsync(context.RunDirectory, context.RunId, step, "parse_error", ex.Message, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"Step {step.Id} ({step.Agent}) failed due to unparseable structured output. {ex.Message}",
                ex);
        }
        catch (Exception ex)
        {
            await this.AppendStepFailureAsync(context.RunDirectory, context.RunId, step, "execution_error", ex.Message, cancellationToken).ConfigureAwait(false);
            throw;
        }
        finally
        {
            this._stateAccessors.AgentExecutionContext.SetCurrent(previousAgentContext);
        }
    }

    internal static void MergeOutcome(ExecutionState state, StepOutcome outcome)
    {
        if (outcome.FilesTouchedDelta.Count > 0)
        {
            state.FilesTouched = state.FilesTouched
                .Concat(outcome.FilesTouchedDelta)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        if (outcome.FrontendPlanDelta is not null)
        {
            state.FrontendPlan = outcome.FrontendPlanDelta;
        }

        if (outcome.Review is not null)
        {
            state.Review = outcome.Review;
        }

        if (outcome.SecurityReview is not null)
        {
            state.SecurityReview = outcome.SecurityReview;
        }

        if (outcome.BuildOutcome is not null)
        {
            state.LastBuildOutcome = outcome.BuildOutcome;
        }
    }

    private Task AppendStepFailureAsync(
        string runDirectory,
        string runId,
        ExecutionPlanStep step,
        string failureType,
        string message,
        CancellationToken cancellationToken)
        => this._artefactStore.AppendEventAsync(runDirectory, new
        {
            runId,
            source = step.Agent,
            status = RunEventStatuses.FAILED,
            failureType,
            stepId = step.Id,
            objective = step.Objective,
            message
        }, cancellationToken);

    private static bool DependenciesSatisfied(
        ExecutionPlanStep step,
        ISet<int> completedStepIds,
        IReadOnlyDictionary<int, ExecutionPlanStep> pendingSteps)
    {
        return step.DependsOnStepIds is null
            || step.DependsOnStepIds.Count == 0
            || step.DependsOnStepIds.All(dep => completedStepIds.Contains(dep) && !pendingSteps.ContainsKey(dep));
    }

    /// <summary>
    /// Contains the aggregated results from executing all plan steps.
    /// </summary>
    public sealed record StepExecutionResult(
        string FrontendPlan,
        IReadOnlyList<string> FilesTouched,
        ArchitectureReview Review,
        SecurityReview SecurityReview,
        BuildOutcome? LastBuildOutcome = null);

    /// <summary>
    /// Mutable aggregate of data produced while steps execute.
    /// </summary>
    public sealed class ExecutionState
    {
        /// <summary>Gets or sets the frontend plan summary.</summary>
        public string FrontendPlan { get; set; } = string.Empty;

        /// <summary>Gets or sets the files touched during execution.</summary>
        public IReadOnlyList<string> FilesTouched { get; set; } = Array.Empty<string>();

        /// <summary>Gets or sets the architecture review.</summary>
        public ArchitectureReview Review { get; set; } = new(Array.Empty<ArchitectureFinding>(), Array.Empty<string>());

        /// <summary>Gets or sets the security review.</summary>
        public SecurityReview SecurityReview { get; set; } = new(Array.Empty<SecurityFinding>(), Array.Empty<string>());

        /// <summary>Gets or sets the last known build outcome.</summary>
        public BuildOutcome? LastBuildOutcome { get; set; }
    }
}
