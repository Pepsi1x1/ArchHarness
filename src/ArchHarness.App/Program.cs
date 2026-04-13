using ArchHarness.App.Agents;
using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using ArchHarness.App.SourceControl;
using ArchHarness.App.Storage;
using ArchHarness.App.Tui;
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
        services.Configure<GitHubOAuthOptions>(configuration.GetSection("gitHubOAuth"));
        services.AddHttpClient();
        services.AddHttpClient<AzureDevOpsSourceControlService>();
        services.AddHttpClient<GitHubSourceControlService>();
        services.AddSingleton<GitHubOAuthDeviceFlowService>();
        services.AddSingleton<IGitHubOAuthDeviceFlowService>(sp => sp.GetRequiredService<GitHubOAuthDeviceFlowService>());
        services.AddSingleton<IRuntimePlatform, RuntimePlatform>();
        services.AddSingleton<ILocalCommandRunner, ProcessLocalCommandRunner>();
        services.AddSingleton<IPersonalAccessTokenProtector, PlatformPersonalAccessTokenProtector>();
        services.AddSingleton<IGitRepositoryInfoService, LibGit2SharpRepositoryInfoService>();
        services.AddSingleton<IGlobalSettingsCatalog, FileSystemGlobalSettingsCatalog>();
        services.AddSingleton<IProviderConnectionCatalog, FileSystemProviderConnectionCatalog>();
        services.AddSingleton<IProviderConnectionSettingsCoordinator, ProviderConnectionSettingsCoordinator>();
        services.AddSingleton<SourceControlProviderFactory>();
        services.AddSingleton<ISourceControlProviderService, SourceControlProviderService>();
        services.AddSingleton<IProjectWorkspaceCatalog, FileSystemProjectWorkspaceCatalog>();
        services.AddSingleton<IRunHistoryCatalog, FileSystemRunHistoryCatalog>();
        services.AddSingleton<IDiscoveredModelCatalog, DiscoveredModelCatalog>();
        services.AddSingleton<ICopilotGovernancePolicy, CopilotGovernancePolicy>();
        services.AddSingleton<IModelResolver, ModelResolver>();
        services.AddSingleton<IStartupPreflightValidator, CopilotStartupPreflightValidator>();
        services.AddSingleton<ICopilotSessionFactory, CopilotSessionFactory>();
        services.AddSingleton<CopilotClientProvider>();
        services.AddSingleton<ICopilotClientProvider>(sp => sp.GetRequiredService<CopilotClientProvider>());
        services.AddSingleton<ICopilotPermissionPromptHandler, InteractivePermissionPromptHandler>();
        services.AddSingleton<CopilotSessionFactory.SessionHooksDependencies>();
        services.AddSingleton<CopilotSessionFactory.CopilotSessionContext>();
        services.AddSingleton<ICopilotClient, CopilotClient>();
        services.AddSingleton<ICopilotSessionEventStream, CopilotSessionEventStream>();
        services.AddSingleton<ICopilotSdkEventStream, CopilotSdkEventStream>();
        services.AddSingleton<IAgentStreamEventStream, AgentStreamEventStream>();
        services.AddSingleton<IAgentToolPolicyProvider, AgentToolPolicyProvider>();
        services.AddSingleton<IRunContextAccessor, RunContextAccessor>();
        services.AddSingleton<IWorkspaceRootAccessor, WorkspaceRootAccessor>();
        services.AddSingleton<IPermissionHandlerModeAccessor, PermissionHandlerModeAccessor>();
        services.AddSingleton<IReviewLoopAgentSelectionAccessor, ReviewLoopAgentSelectionAccessor>();
        services.AddSingleton<IAgentExecutionContextAccessor, AgentExecutionContextAccessor>();
        services.AddSingleton<RuntimeStateAccessors>();
        services.AddSingleton<IToolUsageLogger, ToolUsageLogger>();
        services.AddSingleton<IShellCommandExecutor, ShellCommandExecutor>();
        services.AddSingleton<IVerificationCommandRunner, VerificationCommandRunner>();
        services.AddSingleton<OrchestrationAgent>();
        services.AddSingleton<PlanningAgent>();
        services.AddSingleton<FrontendDeveloperAgent>();
        services.AddSingleton<BackendDeveloperAgent>();
        services.AddSingleton<WikiDocAgent>();
        services.AddSingleton<BuildAgent>();
        services.AddSingleton<CodingStyleAgent>();
        services.AddSingleton<SecurityAgent>();
        services.AddSingleton<ArchitectureAgent>();
        services.AddSingleton<IRunStore, RunStore>();
        services.AddSingleton<IRunStateStore, RunStateStore>();
        services.AddSingleton<IArtefactStore, ArtefactStore>();
        services.AddSingleton<RunSessionContext>();
        services.AddSingleton<OrchestratorRuntime.RunPhaseDependencies>();
        services.AddSingleton<IRunCompletionValidator, RunCompletionValidator>();
        services.AddSingleton<IRunVerificationWorkflow, RunVerificationWorkflow>();
        services.AddSingleton<IRunAgentModelUsageBuilder, RunAgentModelUsageBuilder>();
        services.AddSingleton<OrchestratorRunServices>();
        services.AddSingleton<OrchestratorPlanningServices>();
        services.AddSingleton<IOrchestratedRunProcessor, OrchestratedRunProcessor>();
        services.AddSingleton<ArchitectureReviewLoop.LoopAgentDependencies>();
        services.AddSingleton<ArchitectureReviewLoop>();
        services.AddSingleton<IArchitectureReviewLoop>(sp => sp.GetRequiredService<ArchitectureReviewLoop>());
        services.AddSingleton<AgentStepReviewDispatcher>();
        services.AddSingleton<IAgentStepDispatcher, AgentStepDispatcher>();
        services.AddSingleton<IStepExecutionStateStore, StepExecutionStateStore>();
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
        services.AddSingleton<IWikiDocWorkflow, WikiDocWorkflow>();
        services.AddSingleton<WikiDocRepositoryDiscoverer>();
        services.AddSingleton<WikiDocOutputResolver>();
        services.AddSingleton<IWikiDocMarkdownWriter, WikiDocMarkdownWriter>();
        services.AddSingleton<WikiDocRunServices>();
        services.AddSingleton<SetupSummaryGenerator>();
        services.AddSingleton<RunInfrastructure>();
        services.AddSingleton<OrchestratorRuntime>();
        services.AddSingleton<IOrchestratorRuntime>(sp => sp.GetRequiredService<OrchestratorRuntime>());
        return services;
    }

    /// <summary>
    /// Adds host-agnostic interactive services shared by the console and desktop hosts.
    /// </summary>
    public static IServiceCollection AddArchHarnessInteractiveServices(this IServiceCollection services)
    {
        services.AddSingleton<ISetupStatusSink, NullSetupStatusSink>();
        services.AddSingleton<IUserInputState, UserInputState>();
        services.AddSingleton<IConsoleInputReader, ConsoleInputReader>();
        services.AddSingleton<IChatTerminalRunController, ChatTerminalRunController>();
        services.AddSingleton<IChatTerminalScreenNavigator, ChatTerminalScreenNavigator>();
        services.AddSingleton<ConversationController>();
        return services;
    }
}

