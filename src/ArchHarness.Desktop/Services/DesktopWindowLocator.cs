using Avalonia.Controls;

namespace ArchHarness.Desktop;

public sealed class DesktopWindowLocator : IDesktopWindowLocator
{
    public Window? MainWindow { get; private set; }

    public void SetMainWindow(Window window)
    {
        this.MainWindow = window;
    }
}