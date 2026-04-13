using ArchHarness.App.Agents;

namespace ArchHarness.App.Core;

/// <summary>
/// Groups the agent collaborators used during the planning and verification phases of an orchestrated run.
/// </summary>
public sealed class OrchestratorPlanningServices
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OrchestratorPlanningServices"/> class.
    /// </summary>
    public OrchestratorPlanningServices(
        OrchestrationAgent orchestrationAgent,
        PlanningAgent planningAgent,
        IRunVerificationWorkflow verificationWorkflow)
    {
        this.OrchestrationAgent = orchestrationAgent;
        this.PlanningAgent = planningAgent;
        this.VerificationWorkflow = verificationWorkflow;
    }

    /// <summary>Gets the orchestration agent.</summary>
    public OrchestrationAgent OrchestrationAgent { get; }

    /// <summary>Gets the planning agent.</summary>
    public PlanningAgent PlanningAgent { get; }

    /// <summary>Gets the run verification workflow.</summary>
    public IRunVerificationWorkflow VerificationWorkflow { get; }
}
