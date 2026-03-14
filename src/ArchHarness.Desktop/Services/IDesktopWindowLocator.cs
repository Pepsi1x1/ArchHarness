using Avalonia.Controls;

namespace ArchHarness.Desktop;

public interface IDesktopWindowLocator
{
    Window? MainWindow { get; }

    void SetMainWindow(Window window);
}