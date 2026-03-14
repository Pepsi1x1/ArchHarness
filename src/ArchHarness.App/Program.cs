using ArchHarness.App.Agents;
using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using ArchHarness.App.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ArchHarness.App;

/// <summary>
/// Registers shared ArchHarness runtime and interaction services for host applications.
/// </summary>
public static class ArchHarnessServiceCollectionExtensions
{
	/// <summary>
	/// Adds the core ArchHarness runtime, agent, Copilot, and persistence services.
	/// </summary>
	public static IServiceCollection AddArchHarnessRuntimeServices(this IServiceCollection services, IConfiguration configuration)
	{
		services.Configure<AgentsOptions>(configuration.GetSection("agents"));
		services.Configure<CopilotOptions>(configuration.GetSection("copilot"));
		services.AddHttpClient();
		services.AddSingleton<IDiscoveredModelCatalog, DiscoveredModelCatalog>();
		services.AddSingleton<ICopilotGovernancePolicy, CopilotGovernancePolicy>();
		services.AddSingleton<IModelResolver, ModelResolver>();
		services.AddSingleton<IStartupPreflightValidator, CopilotStartupPreflightValidator>();
		services.AddSingleton<ICopilotSessionFactory, CopilotSessionFactory>();
		services.AddSingleton<CopilotClientProvider>();
		services.AddSingleton<ICopilotClientProvider>(sp => sp.GetRequiredService<CopilotClientProvider>());
		services.AddSingleton<CopilotSessionFactory.SessionHooksDependencies>();
		services.AddSingleton<CopilotSessionFactory.CopilotSessionContext>();
		services.AddSingleton<ICopilotClient, CopilotClient>();
		services.AddSingleton<ICopilotSessionEventStream, CopilotSessionEventStream>();
		services.AddSingleton<IAgentStreamEventStream, AgentStreamEventStream>();
		services.AddSingleton<IAgentToolPolicyProvider, AgentToolPolicyProvider>();
		services.AddSingleton<IRunContextAccessor, RunContextAccessor>();
		services.AddSingleton<IWorkspaceRootAccessor, WorkspaceRootAccessor>();
		services.AddSingleton<IPermissionHandlerModeAccessor, PermissionHandlerModeAccessor>();
		services.AddSingleton<IReviewLoopAgentSelectionAccessor, ReviewLoopAgentSelectionAccessor>();
		services.AddSingleton<IToolUsageLogger, ToolUsageLogger>();
		services.AddSingleton<OrchestrationAgent>();
		services.AddSingleton<FrontendDeveloperAgent>();
		services.AddSingleton<BackendDeveloperAgent>();
		services.AddSingleton<BuildAgent>();
		services.AddSingleton<CodingStyleAgent>();
		services.AddSingleton<SecurityAgent>();
		services.AddSingleton<ArchitectureAgent>();
		services.AddSingleton<IRunStore, RunStore>();
		services.AddSingleton<IArtefactStore, ArtefactStore>();
		services.AddSingleton<OrchestratorRuntime.OrchestratorAgentDependencies>();
		services.AddSingleton<OrchestratorRuntime.RunPhaseDependencies>();
		services.AddSingleton<AgentStepExecutor.StepAgentDependencies>();
		services.AddSingleton<ArchitectureReviewLoop.LoopAgentDependencies>();
		services.AddSingleton<ArchitectureReviewLoop>();
		services.AddSingleton<IArchitectureReviewLoop>(sp => sp.GetRequiredService<ArchitectureReviewLoop>());
		services.AddSingleton<AgentStepExecutor>();
		services.AddSingleton<IAgentStepExecutor>(sp => sp.GetRequiredService<AgentStepExecutor>());
		services.AddSingleton<ExecutionPlanParser>();
		services.AddSingleton<IExecutionPlanParser>(sp => sp.GetRequiredService<ExecutionPlanParser>());
		services.AddSingleton<IWorkspaceContextAnalyzer, WorkspaceContextAnalyzer>();
		services.AddSingleton<RunEventLogger>();
		services.AddSingleton<IRunEventLogger>(sp => sp.GetRequiredService<RunEventLogger>());
		services.AddSingleton<RunArtifactWriter>();
		services.AddSingleton<IRunArtifactWriter>(sp => sp.GetRequiredService<RunArtifactWriter>());
		services.AddSingleton<PlanExecutor>();
		services.AddSingleton<IPlanExecutor>(sp => sp.GetRequiredService<PlanExecutor>());
		services.AddSingleton<SetupSummaryGenerator>();
		services.AddSingleton<RunInfrastructure>();
		services.AddSingleton<OrchestratorRuntime>();
		return services;
	}

	/// <summary>
	/// Adds host-agnostic interactive services shared by the console and desktop hosts.
	/// </summary>
	public static IServiceCollection AddArchHarnessInteractiveServices(this IServiceCollection services)
	{
		services.AddSingleton<IUserInputState, UserInputState>();
		services.AddSingleton<ConversationController>();
		return services;
	}
}
