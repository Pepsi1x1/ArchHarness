namespace ArchHarness.App.SourceControl;

/// <summary>
/// Provides the runtime operating system information.
/// </summary>
public sealed class RuntimePlatform : IRuntimePlatform
{
    /// <inheritdoc />
    public bool IsWindows => OperatingSystem.IsWindows();

    /// <inheritdoc />
    public bool IsMacOS => OperatingSystem.IsMacOS();

    /// <inheritdoc />
    public bool IsLinux => OperatingSystem.IsLinux();
}
