namespace ArchHarness.App.Core;

/// <summary>
/// Describes a plan awaiting user approval, containing the spec and plan to be reviewed.
/// </summary>
/// <param name="Spec">The clarification spec generated for this run.</param>
/// <param name="Plan">The execution plan generated from the spec.</param>
/// <param name="SpecMarkdown">A markdown rendering of the spec for display.</param>
/// <param name="PlanSummary">A human-readable summary of the plan steps.</param>
public sealed record PlanApprovalRequest(
    ClarificationSpec Spec,
    ExecutionPlan Plan,
    string SpecMarkdown,
    string PlanSummary,
    string? PlanReviewMarkdown = null,
    string? PlanningSessionId = null,
    string? RunId = null);

/// <summary>
/// The user's response to a plan approval request.
/// </summary>
/// <param name="Decision">The approval decision (approved, regenerate, canceled).</param>
/// <param name="Reason">Optional reason, especially for regenerate or cancel.</param>
public sealed record PlanApprovalResponse(
    string Decision,
    string? Reason = null);

/// <summary>
/// Host-agnostic bridge that blocks run execution until the user approves, regenerates, or cancels the plan.
/// Implementations exist for the web host (via WebInteractionCoordinator) and console/TUI host.
/// </summary>
public interface IPlanApprovalBridge
{
    /// <summary>
    /// Presents the spec and plan to the user and waits for their approval decision.
    /// </summary>
    Task<PlanApprovalResponse> RequestApprovalAsync(
        PlanApprovalRequest request,
        CancellationToken cancellationToken);
}
