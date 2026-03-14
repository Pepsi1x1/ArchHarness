using ArchHarness.App.Workspace;

namespace ArchHarness.App.Core;

/// <summary>
/// Executes the final build and validates run completion.
/// </summary>
public interface IBuildValidator
{
    /// <summary>
    /// Runs the final build, persists results, and validates run completion.
    /// </summary>
    Task<BuildValidationResult> ExecuteAndValidateAsync(
        ExecutionPlan plan,
        ArchitectureReview review,
        SecurityReview securityReview,
        IWorkspaceAdapter adapter,
        RunRequest request,
        string runId,
        string runDirectory,
        IProgress<RuntimeProgressEvent>? progress,
        CancellationToken cancellationToken);
}
