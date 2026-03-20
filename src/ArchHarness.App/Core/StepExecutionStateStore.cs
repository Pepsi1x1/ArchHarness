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
        string runId,
        string runDirectory,
        int reviewIteration,
        string phase,
        IEnumerable<int> completedStepIds,
        AgentStepExecutor.ExecutionState state,
        string? failureMessage,
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
            SecurityReview = resumeState?.SecurityReview ?? new SecurityReview(Array.Empty<SecurityFinding>(), Array.Empty<string>())
        };

    /// <inheritdoc />
    public Task WriteRunStateAsync(
        string workspaceRoot,
        RunRequest request,
        string runId,
        string runDirectory,
        int reviewIteration,
        string phase,
        IEnumerable<int> completedStepIds,
        AgentStepExecutor.ExecutionState state,
        string? failureMessage,
        CancellationToken cancellationToken)
    {
        PersistedRunState? existingState = this._runStateStore.GetState(runDirectory);
        return this._runStateStore.WriteStateAsync(
            runDirectory,
            new PersistedRunState(
                runId,
                runDirectory,
                workspaceRoot,
                failureMessage is null ? "running" : RunPhases.FAILED,
                phase,
                existingState?.StartedAtUtc ?? DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                request,
                completedStepIds.OrderBy(id => id).ToArray(),
                reviewIteration,
                state.FrontendPlan,
                state.FilesTouched.ToArray(),
                state.Review,
                state.SecurityReview,
                failureMessage),
            cancellationToken);
    }
}
