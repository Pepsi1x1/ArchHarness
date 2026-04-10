using ArchHarness.App.Storage;
using ArchHarness.App.Workspace;

namespace ArchHarness.App.Core;

public sealed record PlanResumeContext(string RunId, string RunDirectory, PersistedRunState? ResumeState);
public sealed record PlanningContext(ClarificationSpec? Spec, IReadOnlyList<ClarificationAnswer>? ClarificationAnswers, string? PlanRevisionRequest = null);

/// <summary>
/// Builds the execution plan via the orchestration agent and dispatches step execution.
/// </summary>
public interface IPlanExecutor
{
    /// <summary>
    /// Builds the execution plan without executing it, so approval can occur before dispatch.
    /// </summary>
    Task<ExecutionPlan> BuildPlanAsync(
        RunRequest request,
        IWorkspaceAdapter adapter,
        string runId,
        string runDirectory,
        PlanningContext? planningContext,
        CancellationToken cancellationToken);

    /// <summary>
    /// Executes an already-built (and approved) plan.
    /// </summary>
    Task<PlanExecutionResult> ExecuteApprovedPlanAsync(
        ExecutionPlan plan,
        RunRequest request,
        IWorkspaceAdapter adapter,
        StepExecutionContext context,
        IProgress<RuntimeProgressEvent>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Builds the execution plan and dispatches all steps to their respective agents.
    /// </summary>
    Task<PlanExecutionResult> BuildAndExecuteAsync(
        RunRequest request,
        IWorkspaceAdapter adapter,
        string runId,
        string runDirectory,
        IProgress<RuntimeProgressEvent>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Executes an existing plan using persisted checkpoint state.
    /// </summary>
    Task<PlanExecutionResult> ExecuteExistingPlanAsync(
        ExecutionPlan plan,
        RunRequest request,
        IWorkspaceAdapter adapter,
        PlanResumeContext context,
        IProgress<RuntimeProgressEvent>? progress,
        CancellationToken cancellationToken);
}
