using ArchHarness.App.Storage;

namespace ArchHarness.App.Core;

/// <summary>
/// Encapsulates all run artifact persistence operations.
/// </summary>
public sealed class RunArtifactWriter
{
    private readonly IArtefactStore _artefactStore;
    private readonly IRunStore _runStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="RunArtifactWriter"/> class.
    /// </summary>
    /// <param name="artefactStore">Store for writing run artefacts.</param>
    /// <param name="runStore">Store for creating run directories and writing run logs.</param>
    public RunArtifactWriter(IArtefactStore artefactStore, IRunStore runStore)
    {
        _artefactStore = artefactStore;
        _runStore = runStore;
    }

    /// <summary>
    /// Creates a new timestamped run directory under the workspace root.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root path.</param>
    /// <returns>The full path to the created run directory.</returns>
    public string CreateRunDirectory(string workspaceRoot)
        => _runStore.CreateRunDirectory(workspaceRoot);

    /// <summary>
    /// Persists the execution plan to the run directory.
    /// </summary>
    /// <param name="runDirectory">The run artefact directory.</param>
    /// <param name="plan">The execution plan to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the async operation.</returns>
    public Task WriteExecutionPlanAsync(string runDirectory, ExecutionPlan plan, CancellationToken cancellationToken)
        => _artefactStore.WriteExecutionPlanAsync(runDirectory, plan, cancellationToken);

    /// <summary>
    /// Persists the architecture review to the run directory.
    /// </summary>
    /// <param name="runDirectory">The run artefact directory.</param>
    /// <param name="review">The architecture review to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the async operation.</returns>
    public Task WriteArchitectureReviewAsync(string runDirectory, ArchitectureReview review, CancellationToken cancellationToken)
        => _artefactStore.WriteArchitectureReviewAsync(runDirectory, review, cancellationToken);

    /// <summary>
    /// Persists the build result to the run directory.
    /// </summary>
    /// <param name="runDirectory">The run artefact directory.</param>
    /// <param name="payload">The build result payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the async operation.</returns>
    public Task WriteBuildResultAsync(string runDirectory, object payload, CancellationToken cancellationToken)
        => _artefactStore.WriteBuildResultAsync(runDirectory, payload, cancellationToken);

    /// <summary>
    /// Persists the final summary to the run directory.
    /// </summary>
    /// <param name="runDirectory">The run artefact directory.</param>
    /// <param name="summary">The summary text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the async operation.</returns>
    public Task WriteFinalSummaryAsync(string runDirectory, string summary, CancellationToken cancellationToken)
        => _artefactStore.WriteFinalSummaryAsync(runDirectory, summary, cancellationToken);

    /// <summary>
    /// Persists the run log to the run directory.
    /// </summary>
    /// <param name="runDirectory">The run artefact directory.</param>
    /// <param name="payload">The run log payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the async operation.</returns>
    public Task WriteRunLogAsync(string runDirectory, object payload, CancellationToken cancellationToken)
        => _runStore.WriteRunLogAsync(runDirectory, payload, cancellationToken);
}
