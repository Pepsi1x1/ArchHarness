using ArchHarness.App.Constants;
using ArchHarness.App.Storage;
using ArchHarness.App.Workspace;

namespace ArchHarness.App.Core;

/// <summary>
/// Executes plan steps in dependency order, delegating step-specific work to focused collaborators.
/// </summary>
public sealed class AgentStepExecutor : IAgentStepExecutor
{
    private const int MAX_NO_CHANGE_CONTINUATIONS = 2;

    private readonly IAgentStepDispatcher _stepDispatcher;
    private readonly IStepExecutionStateStore _stateStore;
    private readonly IArtefactStore _artefactStore;
    private readonly RuntimeStateAccessors _stateAccessors;
    private readonly IContinuationPlanner? _continuationPlanner;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentStepExecutor"/> class.
    /// </summary>
    public AgentStepExecutor(
        IAgentStepDispatcher stepDispatcher,
        IStepExecutionStateStore stateStore,
        IArtefactStore artefactStore,
        RuntimeStateAccessors stateAccessors,
        IContinuationPlanner? continuationPlanner = null)
    {
        this._stepDispatcher = stepDispatcher;
        this._stateStore = stateStore;
        this._artefactStore = artefactStore;
        this._stateAccessors = stateAccessors;
        this._continuationPlanner = continuationPlanner;
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

        // Phase 3b: track full step history so we can grow the plan mid-run with safeguards.
        ContinuationExecutionState continuationState = new ContinuationExecutionState(plan, state.FilesTouched.Count);
        await this._stateStore.WriteRunStateAsync(
            adapter.RootPath,
            request,
            context,
            RunPhases.EXECUTING_PLAN,
            completedStepIds,
            state,
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
                continuationState.RecordOutcome(outcome);
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
                context,
                RunPhases.EXECUTING_PLAN,
                completedStepIds,
                state,
                cancellationToken).ConfigureAwait(false);

            // Phase 3b: after every wave attempt, let the continuation planner grow the plan.
            await this.TryAppendContinuationWaveAsync(
                plan,
                pendingSteps,
                batchOutcomes,
                state,
                context,
                continuationState,
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
        if (pendingSteps.Count == 0)
        {
            return new List<ExecutionPlanStep>();
        }

        int lowestGroup = pendingSteps.Values.Min(s => s.ParallelGroup);
        return pendingSteps.Values
            .Where(candidate => candidate.ParallelGroup == lowestGroup
                && DependenciesSatisfied(candidate, completedStepIds, pendingSteps))
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

    private async Task TryAppendContinuationWaveAsync(
        ExecutionPlan originalPlan,
        Dictionary<int, ExecutionPlanStep> pendingSteps,
        IReadOnlyList<StepOutcome> recentOutcomes,
        ExecutionState state,
        StepExecutionContext context,
        ContinuationExecutionState continuationState,
        CancellationToken cancellationToken)
    {
        if (this._continuationPlanner is null)
        {
            return;
        }

        // Only plan continuation when the current pending queue is empty — otherwise the existing plan
        // still has work to do before we ask for more.
        if (pendingSteps.Count > 0)
        {
            return;
        }

        // Safeguard: explicit-completion check. If every recent outcome self-reported COMPLETE
        // and none surfaced follow-up hints, stop growing the plan.
        // Null/empty CompletionStatus is treated as "unknown" (not complete) so that steps whose
        // dispatcher paths don't populate it (e.g. Build, review) don't prematurely suppress
        // continuation planning.
        bool allComplete = recentOutcomes.All(o =>
            string.Equals(o.CompletionStatus, StepCompletionStatuses.COMPLETE, StringComparison.OrdinalIgnoreCase));
        bool anyHints = recentOutcomes.Any(o => o.FollowUpHints is { Count: > 0 });
        if (allComplete && !anyHints)
        {
            return;
        }

        // Safeguard: honor cancellation before running the planner.
        cancellationToken.ThrowIfCancellationRequested();

        ExecutionPlan snapshot = new ExecutionPlan(continuationState.LiveSteps.ToArray(), originalPlan.IterationStrategy, originalPlan.CompletionCriteria);
        ContinuationPlanningContext planningContext = new ContinuationPlanningContext(
            snapshot,
            recentOutcomes,
            continuationState.OutcomeHistory,
            state.FilesTouched,
            continuationState.PreviousFilesTouchedSnapshot,
            continuationState.NextWave,
            continuationState.NextStepId);

        ContinuationPlanningResult result = this._continuationPlanner.PlanNextWave(planningContext);
        if (result.NewSteps.Count == 0)
        {
            await this._artefactStore.AppendEventAsync(context.RunDirectory, new
            {
                runId = context.RunId,
                source = WellKnownSources.ORCHESTRATOR,
                message = $"Continuation planner produced no new steps ({result.Reason}).",
                wave = continuationState.NextWave
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Safeguard: no-change detection. If the last wave touched no new files, count that as a
        // no-change iteration. Two consecutive no-change iterations abort further continuation.
        bool touchedNewFiles = state.FilesTouched.Count > continuationState.PreviousFilesTouchedSnapshot;
        if (!touchedNewFiles)
        {
            continuationState.NoChangeStreak++;
            if (continuationState.NoChangeStreak >= MAX_NO_CHANGE_CONTINUATIONS)
            {
                await this._artefactStore.AppendEventAsync(context.RunDirectory, new
                {
                    runId = context.RunId,
                    source = WellKnownSources.ORCHESTRATOR,
                    message = $"Continuation aborted after {continuationState.NoChangeStreak} no-change waves.",
                    wave = continuationState.NextWave
                }, cancellationToken).ConfigureAwait(false);
                return;
            }
        }
        else
        {
            continuationState.NoChangeStreak = 0;
        }

        // Safeguard: duplicate-signature detection is handled inside the planner, but double-check
        // here against the live plan before committing the append.
        HashSet<string> liveSignatures = new HashSet<string>(
            continuationState.LiveSteps.Select(s => $"{s.Agent}::{(s.Objective ?? string.Empty).Trim().ToLowerInvariant()}"),
            StringComparer.OrdinalIgnoreCase);

        List<ExecutionPlanStep> accepted = new List<ExecutionPlanStep>();
        foreach (ExecutionPlanStep candidate in result.NewSteps)
        {
            string sig = $"{candidate.Agent}::{(candidate.Objective ?? string.Empty).Trim().ToLowerInvariant()}";
            if (!liveSignatures.Add(sig))
            {
                continue;
            }

            accepted.Add(candidate);
        }

        if (accepted.Count == 0)
        {
            return;
        }

        foreach (ExecutionPlanStep appended in accepted)
        {
            continuationState.LiveSteps.Add(appended);
            pendingSteps.Add(appended.Id, appended);
        }

        await this._artefactStore.AppendEventAsync(context.RunDirectory, new
        {
            runId = context.RunId,
            source = WellKnownSources.ORCHESTRATOR,
            message = $"Continuation planner appended {accepted.Count} step(s) as wave {continuationState.NextWave} ({result.Reason}).",
            wave = continuationState.NextWave,
            stepIds = accepted.Select(s => s.Id).ToArray()
        }, cancellationToken).ConfigureAwait(false);

        continuationState.AdvanceAfterAppend(state.FilesTouched.Count);
    }

    private sealed class ContinuationExecutionState
    {
        public ContinuationExecutionState(ExecutionPlan plan, int previousFilesTouchedSnapshot)
        {
            this.LiveSteps = new List<ExecutionPlanStep>(plan.Steps);
            this.PreviousFilesTouchedSnapshot = previousFilesTouchedSnapshot;
            this.NextWave = this.LiveSteps.Count > 0 ? this.LiveSteps.Max(s => s.Wave) + 1 : 1;
            this.NextStepId = this.LiveSteps.Count > 0 ? this.LiveSteps.Max(s => s.Id) + 1 : 1;
        }

        public List<ExecutionPlanStep> LiveSteps { get; }

        public List<StepOutcome> OutcomeHistory { get; } = new List<StepOutcome>();

        public int NoChangeStreak { get; set; }

        public int PreviousFilesTouchedSnapshot { get; set; }

        public int NextWave { get; set; }

        public int NextStepId { get; set; }

        public void RecordOutcome(StepOutcome outcome)
            => this.OutcomeHistory.Add(outcome);

        public void AdvanceAfterAppend(int filesTouchedCount)
        {
            this.PreviousFilesTouchedSnapshot = filesTouchedCount;
            this.NextWave++;
            this.NextStepId = this.LiveSteps.Max(s => s.Id) + 1;
        }
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
