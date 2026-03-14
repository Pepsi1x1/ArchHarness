using Avalonia.Controls;

namespace ArchHarness.Desktop;

/// <summary>
/// Provides access to the desktop main window for child components that need a parent reference.
/// </summary>
public interface IDesktopWindowLocator
{
    /// <summary>Gets the main application window, or <see langword="null"/> if not yet registered.</summary>
    Window? MainWindow { get; }

    /// <summary>
    /// Registers the main window instance for later retrieval.
    /// </summary>
    /// <param name="window">The main window to register.</param>
    void SetMainWindow(Window window);
}