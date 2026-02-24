using ArchHarness.App.Agents;
using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using ArchHarness.App.Storage;
using ArchHarness.App.Tui;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

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
builder.Services.AddSingleton<CopilotSessionFactory.CopilotSessionContext>();
builder.Services.AddSingleton<ICopilotClient, CopilotClient>();
builder.Services.AddSingleton<ICopilotSessionEventStream, CopilotSessionEventStream>();
builder.Services.AddSingleton<IAgentStreamEventStream, AgentStreamEventStream>();
builder.Services.AddSingleton<IAgentToolPolicyProvider, AgentToolPolicyProvider>();
builder.Services.AddSingleton<IRunContextAccessor, RunContextAccessor>();
builder.Services.AddSingleton<IToolUsageLogger, ToolUsageLogger>();
builder.Services.AddSingleton<OrchestrationAgent>();
builder.Services.AddSingleton<FrontendAgent>();
builder.Services.AddSingleton<BuilderAgent>();
builder.Services.AddSingleton<ArchitectureAgent>();
builder.Services.AddSingleton<IRunStore, RunStore>();
builder.Services.AddSingleton<IArtefactStore, ArtefactStore>();
builder.Services.AddSingleton<IBuildRunner, BuildRunner>();
builder.Services.AddSingleton<OrchestratorRuntime.OrchestratorAgentDependencies>();
builder.Services.AddSingleton<OrchestratorRuntime.OrchestratorServiceDependencies>();
builder.Services.AddSingleton<ArchitectureReviewLoop>();
builder.Services.AddSingleton<AgentStepExecutor>();
builder.Services.AddSingleton<OrchestratorRuntime>();
builder.Services.AddSingleton<ConversationController>();
builder.Services.AddSingleton<ChatTerminal>();

using var host = builder.Build();
var sessionFactory = host.Services.GetRequiredService<ICopilotSessionFactory>() as CopilotSessionFactory;
if (sessionFactory is not null)
{
    var orchestrationAgent = host.Services.GetRequiredService<OrchestrationAgent>();
    _ = sessionFactory.WarmUpAsync(
        orchestrationAgent.DefaultModel,
        orchestrationAgent.GetWarmUpCompletionOptions());
}

var terminal = host.Services.GetRequiredService<ChatTerminal>();
await terminal.RunAsync(args);
