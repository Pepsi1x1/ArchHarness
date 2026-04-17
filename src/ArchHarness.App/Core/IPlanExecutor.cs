using ArchHarness.App.Storage;
using ArchHarness.App.Workspace;

namespace ArchHarness.App.Core;

public sealed record PlanResumeContext(string RunId, string RunDirectory, PersistedRunState? ResumeState);

/// <summary>
/// Inputs the plan executor uses when building or revising a plan.
/// </summary>
/// <param name="Spec">The active clarification spec, if one has been produced.</param>
/// <param name="ClarificationAnswers">The ordered clarification question/answer pairs collected so far.</param>
/// <param name="PlanRevisionRequest">Optional free-form plan-revision instruction supplied by the user.</param>
/// <param name="ConversationHistory">The planning-session conversation ledger up to (but not including) the current plan build. Used so the planning agent can consume chat history, attachment context, and post-handoff follow-up messages.</param>
/// <param name="PlanningSessionId">Optional durable planning-session identifier linking this plan build to a shared session.</param>
/// <param name="Attachments">Optional prompt attachments accompanying the latest user message (e.g., images).</param>
public sealed record PlanningContext(
    ClarificationSpec? Spec,
    IReadOnlyList<ClarificationAnswer>? ClarificationAnswers,
    string? PlanRevisionRequest = null,
    IReadOnlyList<ConversationMessage>? ConversationHistory = null,
    string? PlanningSessionId = null,
    IReadOnlyList<PromptAttachment>? Attachments = null);

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
