namespace ArchHarness.App.Core;

/// <summary>
/// Default setup status sink for hosts that do not render setup progress inline.
/// </summary>
public sealed class NullSetupStatusSink : ISetupStatusSink
{
    /// <inheritdoc />
    public void Clear()
    {
    }

    /// <inheritdoc />
    public void WriteLine(string message)
    {
    }
}