namespace ArchHarness.App.SourceControl;

/// <summary>
/// Represents the outcome of testing a provider connection.
/// </summary>
public sealed record ConnectionTestResult(bool Success, string Message);
