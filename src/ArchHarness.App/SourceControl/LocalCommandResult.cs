namespace ArchHarness.App.SourceControl;

/// <summary>
/// Captures the result of running a local command.
/// </summary>
public sealed record LocalCommandResult(int ExitCode, string StandardOutput, string StandardError);