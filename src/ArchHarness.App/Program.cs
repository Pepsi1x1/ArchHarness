using ArchHarness.App.Agents;
using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using ArchHarness.App.Tui;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

var orchestrationModel = builder.Configuration["agents:orchestration:model"] ?? "sonnet-4.6";
var frontendModel = builder.Configuration["agents:frontend:model"] ?? "sonnet-4.6";
var builderModel = builder.Configuration["agents:builder:model"] ?? "codex-5.3";
var architectureModel = builder.Configuration["agents:architecture:model"] ?? "opus-4.6";

builder.Services.AddSingleton<CopilotSessionFactory>();
builder.Services.AddSingleton<CopilotClient>();
builder.Services.AddSingleton(sp => new OrchestrationAgent(sp.GetRequiredService<CopilotClient>(), orchestrationModel));
builder.Services.AddSingleton(sp => new FrontendAgent(sp.GetRequiredService<CopilotClient>(), frontendModel));
builder.Services.AddSingleton(sp => new BuilderAgent(sp.GetRequiredService<CopilotClient>(), builderModel));
builder.Services.AddSingleton(sp => new ArchitectureAgent(sp.GetRequiredService<CopilotClient>(), architectureModel));
builder.Services.AddSingleton<OrchestratorRuntime>();
builder.Services.AddSingleton<ChatTerminal>();

using var host = builder.Build();
var terminal = host.Services.GetRequiredService<ChatTerminal>();
await terminal.RunAsync(args);
