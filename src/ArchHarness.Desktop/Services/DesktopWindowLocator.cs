using Avalonia.Controls;

namespace ArchHarness.Desktop;

/// <summary>
/// Default implementation of <see cref="IDesktopWindowLocator"/> that holds a reference to the main window.
/// </summary>
public sealed class DesktopWindowLocator : IDesktopWindowLocator
{
    /// <inheritdoc />
    public Window? MainWindow { get; private set; }

    /// <inheritdoc />
    public void SetMainWindow(Window window)
    {
        this.MainWindow = window;
    }
}