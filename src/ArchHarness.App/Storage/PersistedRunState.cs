using ArchHarness.App.Core;

namespace ArchHarness.App.Storage;

/// <summary>
/// Represents the durable checkpoint for a resumable run.
/// </summary>
public sealed record PersistedRunState(
    string RunId,
    string RunDirectory,
    string WorkspaceRoot,
    string Status,
    string Phase,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    RunRequest Request,
    int[] CompletedStepIds,
    int ReviewIteration,
    string FrontendPlan,
    string[] FilesTouched,
    ArchitectureReview Review,
    SecurityReview SecurityReview,
    string? FailureMessage = null)
{
    public bool CanResume
        => !string.Equals(this.Status, RunStatuses.COMPLETED, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(this.Status, RunStatuses.CANCELED, StringComparison.OrdinalIgnoreCase);
}