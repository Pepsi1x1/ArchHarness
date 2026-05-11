namespace ArchHarness.App.Core;

/// <summary>
/// Groups the long-lived collaborators used during an orchestrated run.
/// </summary>
public sealed class OrchestratorRunServices
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OrchestratorRunServices"/> class.
    /// </summary>
    public OrchestratorRunServices(
        RunSessionContext sessionContext,
        RunInfrastructure runInfrastructure,
        OrchestratorRuntime.RunPhaseDependencies runPhases,
        IRunAgentModelUsageBuilder agentModelUsageBuilder)
    {
        this.SessionContext = sessionContext;
        this.RunInfrastructure = runInfrastructure;
        this.RunPhases = runPhases;
        this.AgentModelUsageBuilder = agentModelUsageBuilder;
    }

    /// <summary>Gets the session-scoped runtime services.</summary>
    public RunSessionContext SessionContext { get; }

    /// <summary>Gets the run infrastructure services.</summary>
    public RunInfrastructure RunInfrastructure { get; }

    /// <summary>Gets the run-phase services.</summary>
    public OrchestratorRuntime.RunPhaseDependencies RunPhases { get; }

    /// <summary>Gets the model usage projection service for run logs.</summary>
    public IRunAgentModelUsageBuilder AgentModelUsageBuilder { get; }
}
