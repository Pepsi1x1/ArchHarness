using ArchHarness.App.Agents;

namespace ArchHarness.App.Core;

/// <summary>
/// Groups planner and orchestration-time collaborators used around an orchestrated run.
/// </summary>
public sealed class OrchestratorPlanningServices
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OrchestratorPlanningServices"/> class.
    /// </summary>
    public OrchestratorPlanningServices(
        OrchestrationAgent orchestrationAgent,
        PlanningAgent planningAgent,
        IRunVerificationWorkflow verificationWorkflow,
        IContinuationPlanner? continuationPlanner = null)
    {
        this.OrchestrationAgent = orchestrationAgent;
        this.PlanningAgent = planningAgent;
        this.VerificationWorkflow = verificationWorkflow;
        this.ContinuationPlanner = continuationPlanner ?? new DeterministicContinuationPlanner();
    }

    /// <summary>Gets the orchestration agent used for execution-time orchestration, remediation, verification, and replanning.</summary>
    public OrchestrationAgent OrchestrationAgent { get; }

    /// <summary>Gets the planner agent used for initial Planning mode clarification and plan generation.</summary>
    public PlanningAgent PlanningAgent { get; }

    /// <summary>Gets the run verification workflow.</summary>
    public IRunVerificationWorkflow VerificationWorkflow { get; }

    /// <summary>Gets the planner used to promote structured follow-up hints into appended waves.</summary>
    public IContinuationPlanner ContinuationPlanner { get; }
}
