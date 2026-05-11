namespace ArchHarness.Web.Services;

/// <summary>
/// Represents a pending host interaction that requires user action.
/// </summary>
public sealed record PendingInteractionSnapshot(
    string Kind,
    string Question,
    IReadOnlyList<string> Choices,
    string? PermissionKind,
    string? SessionId,
    string? ToolName,
    string? SpecMarkdown = null,
    string? PlanSummary = null,
    string? PlanReviewMarkdown = null,
    string? RunId = null,
    IReadOnlyList<string>? Questions = null);
