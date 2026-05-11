namespace ArchHarness.App.Core;

/// <summary>
/// Writes high-level setup progress updates for interactive hosts.
/// </summary>
public interface ISetupStatusSink
{
    /// <summary>
    /// Clears the active setup status surface.
    /// </summary>
    void Clear();

    /// <summary>
    /// Writes a single setup status line.
    /// </summary>
    /// <param name="message">The message to write.</param>
    void WriteLine(string message);
}
