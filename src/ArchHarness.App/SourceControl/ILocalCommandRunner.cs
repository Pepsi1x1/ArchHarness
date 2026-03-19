namespace ArchHarness.App.SourceControl;

/// <summary>
/// Runs local commands needed for platform-native credential storage.
/// </summary>
public interface ILocalCommandRunner
{
    /// <summary>
    /// Gets a value indicating whether the specified command is available on PATH.
    /// </summary>
    bool IsCommandAvailable(string commandName);

    /// <summary>
    /// Runs a local command and captures its output.
    /// </summary>
    LocalCommandResult Run(string commandName, IReadOnlyList<string> arguments, string? standardInput = null);
}