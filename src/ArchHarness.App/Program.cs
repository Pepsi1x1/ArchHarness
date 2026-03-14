using ArchHarness.App.Agents;
using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using ArchHarness.App.Storage;
using ArchHarness.App.Tui;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

if (ShadowRuntimeBootstrap.TryRelaunchFromShadowCopy(args))
{
    return;
}

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<AgentsOptions>(builder.Configuration.GetSection("agents"));
builder.Services.Configure<CopilotOptions>(builder.Configuration.GetSection("copilot"));
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IDiscoveredModelCatalog, DiscoveredModelCatalog>();
builder.Services.AddSingleton<ICopilotGovernancePolicy, CopilotGovernancePolicy>();
builder.Services.AddSingleton<IUserInputState, UserInputState>();
builder.Services.AddSingleton<ICopilotUserInputBridge, ConsoleCopilotUserInputBridge>();
builder.Services.AddSingleton<IModelResolver, ModelResolver>();
builder.Services.AddSingleton<IStartupPreflightValidator, CopilotStartupPreflightValidator>();
builder.Services.AddSingleton<ICopilotSessionFactory, CopilotSessionFactory>();
builder.Services.AddSingleton<CopilotClientProvider>();
builder.Services.AddSingleton<ICopilotClientProvider>(sp => sp.GetRequiredService<CopilotClientProvider>());
builder.Services.AddSingleton<CopilotSessionFactory.SessionHooksDependencies>();
builder.Services.AddSingleton<CopilotSessionFactory.CopilotSessionContext>();
builder.Services.AddSingleton<ICopilotClient, CopilotClient>();
builder.Services.AddSingleton<ICopilotSessionEventStream, CopilotSessionEventStream>();
builder.Services.AddSingleton<IAgentStreamEventStream, AgentStreamEventStream>();
builder.Services.AddSingleton<IAgentToolPolicyProvider, AgentToolPolicyProvider>();
builder.Services.AddSingleton<IRunContextAccessor, RunContextAccessor>();
builder.Services.AddSingleton<IWorkspaceRootAccessor, WorkspaceRootAccessor>();
builder.Services.AddSingleton<IToolUsageLogger, ToolUsageLogger>();
builder.Services.AddSingleton<OrchestrationAgent>();
builder.Services.AddSingleton<FrontendDeveloperAgent>();
builder.Services.AddSingleton<BackendDeveloperAgent>();
builder.Services.AddSingleton<BuildAgent>();
builder.Services.AddSingleton<CodingStyleAgent>();
builder.Services.AddSingleton<SecurityAgent>();
builder.Services.AddSingleton<ArchitectureAgent>();
builder.Services.AddSingleton<IRunStore, RunStore>();
builder.Services.AddSingleton<IArtefactStore, ArtefactStore>();
builder.Services.AddSingleton<OrchestratorRuntime.OrchestratorAgentDependencies>();
builder.Services.AddSingleton<OrchestratorRuntime.RunPhaseDependencies>();
builder.Services.AddSingleton<AgentStepExecutor.StepAgentDependencies>();
builder.Services.AddSingleton<ArchitectureReviewLoop.LoopAgentDependencies>();
builder.Services.AddSingleton<ArchitectureReviewLoop>();
builder.Services.AddSingleton<IArchitectureReviewLoop>(sp => sp.GetRequiredService<ArchitectureReviewLoop>());
builder.Services.AddSingleton<AgentStepExecutor>();
builder.Services.AddSingleton<IAgentStepExecutor>(sp => sp.GetRequiredService<AgentStepExecutor>());
builder.Services.AddSingleton<ExecutionPlanParser>();
builder.Services.AddSingleton<IExecutionPlanParser>(sp => sp.GetRequiredService<ExecutionPlanParser>());
builder.Services.AddSingleton<IWorkspaceContextAnalyzer, WorkspaceContextAnalyzer>();
builder.Services.AddSingleton<RunEventLogger>();
builder.Services.AddSingleton<IRunEventLogger>(sp => sp.GetRequiredService<RunEventLogger>());
builder.Services.AddSingleton<RunArtifactWriter>();
builder.Services.AddSingleton<IRunArtifactWriter>(sp => sp.GetRequiredService<RunArtifactWriter>());
builder.Services.AddSingleton<PlanExecutor>();
builder.Services.AddSingleton<IPlanExecutor>(sp => sp.GetRequiredService<PlanExecutor>());
builder.Services.AddSingleton<SetupSummaryGenerator>();
builder.Services.AddSingleton<RunInfrastructure>();
builder.Services.AddSingleton<OrchestratorRuntime>();
builder.Services.AddSingleton<ConversationController>();
builder.Services.AddSingleton<ChatTerminal>();

using IHost host = builder.Build();
ChatTerminal terminal = host.Services.GetRequiredService<ChatTerminal>();
await terminal.RunAsync(args);
