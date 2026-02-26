using ArchHarness.App.Agents;
using ArchHarness.App.Workspace;

namespace ArchHarness.App.Core;

/// <summary>
/// Result of plan building and step execution.
/// </summary>
/// <param name="Plan">The built execution plan.</param>
/// <param name="StepResult">The aggregated results from executing all plan steps.</param>
public sealed record PlanExecutionResult(
    ExecutionPlan Plan,
    AgentStepExecutor.StepExecutionResult StepResult);

/// <summary>
/// Builds the execution plan via the orchestration agent and dispatches step execution.
/// </summary>
public sealed class PlanExecutor
{
    private const string ORCHESTRATOR_SOURCE = "orchestrator";

    private readonly OrchestrationAgent _orchestrationAgent;
    private readonly AgentStepExecutor _agentStepExecutor;
    private readonly RunEventLogger _eventLogger;
    private readonly RunArtifactWriter _artifactWriter;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlanExecutor"/> class.
    /// </summary>
    /// <param name="orchestrationAgent">Agent used to build execution plans.</param>
    /// <param name="agentStepExecutor">Executor that dispatches individual plan steps.</param>
    /// <param name="eventLogger">Logger for run events.</param>
    /// <param name="artifactWriter">Writer for run artifacts.</param>
    public PlanExecutor(
        OrchestrationAgent orchestrationAgent,
        AgentStepExecutor agentStepExecutor,
        RunEventLogger eventLogger,
        RunArtifactWriter artifactWriter)
    {
        _orchestrationAgent = orchestrationAgent;
        _agentStepExecutor = agentStepExecutor;
        _eventLogger = eventLogger;
        _artifactWriter = artifactWriter;
    }

    /// <summary>
    /// Builds the execution plan and dispatches all steps to their respective agents.
    /// </summary>
    /// <param name="request">The originating run request.</param>
    /// <param name="adapter">Workspace adapter for file and diff operations.</param>
    /// <param name="runId">Unique identifier for the current run.</param>
    /// <param name="runDirectory">Directory where run artefacts are stored.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Token to signal cancellation.</param>
    /// <returns>The plan and aggregated step execution results.</returns>
    public async Task<PlanExecutionResult> BuildAndExecuteAsync(
        RunRequest request,
        IWorkspaceAdapter adapter,
        string runId,
        string runDirectory,
        IProgress<RuntimeProgressEvent>? progress,
        CancellationToken cancellationToken)
    {
        ExecutionPlan plan = await _orchestrationAgent.BuildExecutionPlanAsync(
            request,
            adapter.RootPath,
            _orchestrationAgent.Id,
            _orchestrationAgent.Role,
            cancellationToken);

        await _eventLogger.AppendEventAsync(runDirectory, new { runId, source = ORCHESTRATOR_SOURCE, message = "Execution plan built" }, cancellationToken);
        await _artifactWriter.WriteExecutionPlanAsync(runDirectory, plan, cancellationToken);

        AgentStepExecutor.StepExecutionResult stepResult = await _agentStepExecutor.ExecuteAsync(
            plan,
            adapter,
            request,
            runId,
            runDirectory,
            progress,
            cancellationToken);

        return new PlanExecutionResult(plan, stepResult);
    }
}
