using ArchHarness.App;
using ArchHarness.App.Copilot;
using ArchHarness.Desktop.ViewModels;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ArchHarness.Desktop;

/// <summary>
/// Entry point for the Avalonia desktop host application.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Application entry point that builds the DI host and starts the Avalonia lifetime.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    [STAThread]
    public static void Main(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddArchHarnessRuntimeServices(builder.Configuration);
        builder.Services.AddArchHarnessInteractiveServices();
        builder.Services.AddSingleton<IDesktopWindowLocator, DesktopWindowLocator>();
        builder.Services.AddSingleton<ICopilotUserInputBridge, DesktopCopilotUserInputBridge>();
        builder.Services.AddSingleton<IRunHistoryService, RunHistoryService>();
        builder.Services.AddSingleton<AgentTranscriptAggregator>();
        builder.Services.AddSingleton<RunStreamingCoordinator>();
        builder.Services.AddSingleton<MainWindowViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        using IHost host = builder.Build();
        DesktopHostContext.CurrentHost = host;

        BuildAvaloniaApp(host).StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Configures the Avalonia application builder with the DI host.
    /// </summary>
    /// <param name="host">The built application host.</param>
    /// <returns>The configured Avalonia app builder.</returns>
    public static AppBuilder BuildAvaloniaApp(IHost host)
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}