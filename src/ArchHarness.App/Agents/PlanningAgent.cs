using ArchHarness.App.Core;
using ArchHarness.App.Copilot;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Agents;

/// <summary>
/// Planning-specialized orchestration agent that shares orchestration logic with a dedicated model and reasoning profile.
/// </summary>
public sealed class PlanningAgent : OrchestrationAgent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PlanningAgent"/> class.
    /// </summary>
    public PlanningAgent(
        ICopilotClient copilotClient,
        IModelResolver modelResolver,
        IAgentToolPolicyProvider toolPolicyProvider,
        IOptions<AgentsOptions> agentsOptions,
        IReviewLoopAgentSelectionAccessor reviewLoopAgentSelectionAccessor,
        IExecutionPlanParser executionPlanParser)
        : base(copilotClient, modelResolver, toolPolicyProvider, agentsOptions, reviewLoopAgentSelectionAccessor, executionPlanParser, "planning")
    {
    }
}