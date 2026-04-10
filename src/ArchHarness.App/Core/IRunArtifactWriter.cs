namespace ArchHarness.App.Core;

/// <summary>
/// Encapsulates all run artifact persistence operations.
/// </summary>
public interface IRunArtifactWriter
{
    /// <summary>
    /// Creates a new timestamped run directory under the workspace root.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root path.</param>
    /// <returns>The full path to the created run directory.</returns>
    string CreateRunDirectory(string workspaceRoot);

    /// <summary>
    /// Persists the execution plan to the run directory.
    /// </summary>
    Task WriteExecutionPlanAsync(string runDirectory, ExecutionPlan plan, CancellationToken cancellationToken);

    /// <summary>
    /// Persists the architecture review to the run directory.
    /// </summary>
    Task WriteArchitectureReviewAsync(string runDirectory, ArchitectureReview review, CancellationToken cancellationToken);

    /// <summary>
    /// Persists the security review to the run directory.
    /// </summary>
    Task WriteSecurityReviewAsync(string runDirectory, SecurityReview review, CancellationToken cancellationToken);

    /// <summary>
    /// Persists the build result to the run directory.
    /// </summary>
    Task WriteBuildResultAsync(string runDirectory, object payload, CancellationToken cancellationToken);

    /// <summary>
    /// Persists the completion validation result to the run directory as JSON and Markdown.
    /// </summary>
    Task WriteCompletionValidationAsync(string runDirectory, CompletionValidationResult validation, CancellationToken cancellationToken);

    /// <summary>
    /// Persists the final summary to the run directory.
    /// </summary>
    Task WriteFinalSummaryAsync(string runDirectory, string summary, CancellationToken cancellationToken);

    /// <summary>
    /// Persists the run log to the run directory.
    /// </summary>
    Task WriteRunLogAsync(string runDirectory, object payload, CancellationToken cancellationToken);

    /// <summary>
    /// Persists the clarification spec as both JSON and Markdown to the run directory.
    /// </summary>
    Task WriteClarificationSpecAsync(string runDirectory, ClarificationSpec spec, CancellationToken cancellationToken);

    /// <summary>
    /// Persists the plan approval decision to the run directory.
    /// </summary>
    Task WritePlanApprovalAsync(string runDirectory, PlanApproval approval, CancellationToken cancellationToken);
}
