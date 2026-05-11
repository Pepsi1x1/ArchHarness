namespace ArchHarness.App.SourceControl;

/// <summary>
/// Exposes the current operating system for platform-specific integrations.
/// </summary>
public interface IRuntimePlatform
{
    /// <summary>
    /// Gets a value indicating whether the current platform is Windows.
    /// </summary>
    bool IsWindows { get; }

    /// <summary>
    /// Gets a value indicating whether the current platform is macOS.
    /// </summary>
    bool IsMacOS { get; }

    /// <summary>
    /// Gets a value indicating whether the current platform is Linux.
    /// </summary>
    bool IsLinux { get; }
}
