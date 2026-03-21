namespace ArchHarness.Web.Services;

/// <summary>
/// Represents a web-consumable event emitted while a run is active.
/// </summary>
public sealed record WebRunEvent(
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