namespace ArchHarness.Web.Services;

/// <summary>
/// Payload for completing a pending user-input interaction.
/// </summary>
/// <param name="Answer">The submitted answer text.</param>
/// <param name="Answers">The submitted answer texts for a batched interaction.</param>
public sealed record UserInputSubmission(string? Answer, IReadOnlyList<string>? Answers = null);

/// <summary>
/// Payload for completing a pending permission interaction.
/// </summary>
/// <param name="Approved">Whether the request is approved.</param>
public sealed record PermissionSubmission(bool Approved);

/// <summary>
/// Payload for completing a pending plan-approval interaction.
/// </summary>
/// <param name="Decision">The approval decision: approved, regenerate, or canceled.</param>
/// <param name="Reason">Optional reason for the decision.</param>
public sealed record PlanApprovalSubmission(string Decision, string? Reason = null);