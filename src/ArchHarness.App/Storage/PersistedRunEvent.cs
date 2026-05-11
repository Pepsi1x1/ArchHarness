namespace ArchHarness.App.Storage;

/// <summary>
/// Represents a replayable event read from a persisted run log.
/// </summary>
public sealed record PersistedRunEvent(
    DateTimeOffset TimestampUtc,
    string Kind,
    string Source,
    string Message,
    string? AgentId = null,
    string? AgentRole = null,
    string? SessionId = null,
    string? Model = null,
    string? Details = null,
    string? ContentFormat = null,
    string? StreamKind = null,
    string? Title = null,
    string? TaskPrompt = null);
