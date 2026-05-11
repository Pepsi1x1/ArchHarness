namespace ArchHarness.App.Core;

/// <summary>
/// Defines a UI host capable of running the ArchHarness interaction lifecycle.
/// </summary>
public interface IApplicationHost
{
    /// <summary>
    /// Runs the host using the provided command-line arguments.
    /// </summary>
    Task RunAsync(string[] args, CancellationToken cancellationToken = default);
}
