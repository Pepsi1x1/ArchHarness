using ArchHarness.App.Storage;

namespace ArchHarness.App.Core;

/// <summary>
/// Builds and persists resumable step-execution state.
/// </summary>
public interface IStepExecutionStateStore
{
    /// <summary>
    /// Creates mutable execution state from an optional resume checkpoint.
    /// </summary>
    AgentStepExecutor.ExecutionState CreateExecutionState(PersistedRunState? resumeState);

    /// <summary>
    /// Writes the current execution checkpoint to disk.
    /// </summary>
    Task WriteRunStateAsync(
        string workspaceRoot,
        RunRequest request,
        StepExecutionContext context,
        string phase,
        IEnumerable<int> completedStepIds,
        AgentStepExecutor.ExecutionState state,
        CancellationToken cancellationToken);
}

/// <summary>
/// Default implementation of <see cref="IStepExecutionStateStore"/>.
/// </summary>
public sealed class StepExecutionStateStore : IStepExecutionStateStore
{
    private readonly IRunStateStore _runStateStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="StepExecutionStateStore"/> class.
    /// </summary>
    public StepExecutionStateStore(IRunStateStore runStateStore)
    {
        this._runStateStore = runStateStore;
    }

    /// <inheritdoc />
    public AgentStepExecutor.ExecutionState CreateExecutionState(PersistedRunState? resumeState)
        => new()
        {
            FrontendPlan = resumeState?.FrontendPlan ?? string.Empty,
            FilesTouched = resumeState?.FilesTouched ?? Array.Empty<string>(),
            Review = resumeState?.Review ?? new ArchitectureReview(Array.Empty<ArchitectureFinding>(), Array.Empty<string>()),
            SecurityReview = resumeState?.SecurityReview ?? new SecurityReview(Array.Empty<SecurityFinding>(), Array.Empty<string>()),
            LastBuildOutcome = resumeState?.LastBuildOutcome
        };

    /// <inheritdoc />
    public Task WriteRunStateAsync(
        string workspaceRoot,
        RunRequest request,
        StepExecutionContext context,
        string phase,
        IEnumerable<int> completedStepIds,
        AgentStepExecutor.ExecutionState state,
        CancellationToken cancellationToken)
    {
        return this._runStateStore.UpdateStateAsync(
            context.RunDirectory,
            existingState => new PersistedRunState(
                context.RunId,
                context.RunDirectory,
                workspaceRoot,
                RunStatuses.RUNNING,
                phase,
                existingState?.StartedAtUtc ?? DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                request,
                completedStepIds.OrderBy(id => id).ToArray(),
                context.ResumeState?.ReviewIteration ?? 0,
                state.FrontendPlan,
                state.FilesTouched.ToArray(),
                state.Review,
                state.SecurityReview,
                FailureMessage: null,
                Spec: existingState?.Spec,
                Approval: existingState?.Approval,
                LastBuildOutcome: state.LastBuildOutcome ?? existingState?.LastBuildOutcome,
                CompletionValidation: existingState?.CompletionValidation,
                ClarificationAnswers: existingState?.ClarificationAnswers,
                HandoffRunId: existingState?.HandoffRunId,
                PlanningSessionId: existingState?.PlanningSessionId ?? request.PlanningSessionId,
                ConversationHistory: existingState?.ConversationHistory,
                CurrentWave: existingState?.CurrentWave ?? 0),
            cancellationToken);
    }
}
