using ArchHarness.App;
using ArchHarness.App.Copilot;
using Avalonia;
using ArchHarness.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ArchHarness.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddArchHarnessRuntimeServices(builder.Configuration);
        builder.Services.AddArchHarnessInteractiveServices();
        builder.Services.AddSingleton<IDesktopWindowLocator, DesktopWindowLocator>();
        builder.Services.AddSingleton<ICopilotUserInputBridge, DesktopCopilotUserInputBridge>();
        builder.Services.AddSingleton<IRunHistoryService, RunHistoryService>();
        builder.Services.AddSingleton<MainWindowViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        using IHost host = builder.Build();
        DesktopHostContext.CurrentHost = host;

        BuildAvaloniaApp(host).StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp(IHost host)
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}