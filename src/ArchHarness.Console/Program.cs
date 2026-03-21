using ArchHarness.App;
using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using ArchHarness.App.Tui;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddArchHarnessRuntimeServices(builder.Configuration);
builder.Services.AddArchHarnessInteractiveServices();
builder.Services.AddSingleton<ISetupStatusSink, ConsoleSetupStatusSink>();
builder.Services.AddSingleton<ICopilotUserInputBridge, ConsoleCopilotUserInputBridge>();
builder.Services.AddSingleton<ChatTerminal>();
builder.Services.AddSingleton<IApplicationHost>(sp => sp.GetRequiredService<ChatTerminal>());

using IHost host = builder.Build();
IApplicationHost applicationHost = host.Services.GetRequiredService<IApplicationHost>();
await applicationHost.RunAsync(args);
