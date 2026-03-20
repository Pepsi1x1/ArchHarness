using ArchHarness.App.Storage;
using ArchHarness.App.Workspace;

namespace ArchHarness.App.Core;

public sealed record StepExecutionContext(string RunId, string RunDirectory, PersistedRunState? ResumeState);

/// <summary>
/// Executes plan steps in dependency order, dispatching each step to the appropriate agent.
/// </summary>
public interface IAgentStepExecutor
{
    /// <summary>
    /// Executes all steps in the execution plan in dependency order, dispatching each to the
    /// correct agent and tracking completion.
    /// </summary>
    Task<AgentStepExecutor.StepExecutionResult> ExecuteAsync(
        ExecutionPlan plan,
        IWorkspaceAdapter adapter,
        RunRequest request,
        StepExecutionContext context,
        IProgress<RuntimeProgressEvent>? progress,
        CancellationToken cancellationToken);
}
