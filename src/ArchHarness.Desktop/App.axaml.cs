using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ArchHarness.Desktop;

/// <summary>
/// The Avalonia application entry point that wires up the DI host and main window.
/// </summary>
public partial class App : Application
{
    private readonly IHost? _host;

    public App()
    {
        this._host = DesktopHostContext.CurrentHost;
    }

    public App(IHost host)
    {
        this._host = host;
    }

    /// <inheritdoc />
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (this._host is not null && this.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow mainWindow = this._host.Services.GetRequiredService<MainWindow>();
            IDesktopWindowLocator windowLocator = this._host.Services.GetRequiredService<IDesktopWindowLocator>();
            windowLocator.SetMainWindow(mainWindow);
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}