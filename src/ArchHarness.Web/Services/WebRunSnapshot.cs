namespace ArchHarness.Web.Services;

/// <summary>
/// Captures the current state of the locally hosted run session.
/// </summary>
public sealed record WebRunSnapshot(
    bool IsRunning,
    string Status,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? RunId,
    string? RunDirectory,
    string? TaskPrompt,
    string? WorkspacePath,
    string? FailureMessage);