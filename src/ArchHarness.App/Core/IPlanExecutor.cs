using ArchHarness.App.Workspace;

namespace ArchHarness.App.Core;

/// <summary>
/// Builds the execution plan via the orchestration agent and dispatches step execution.
/// </summary>
public interface IPlanExecutor
{
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
}
