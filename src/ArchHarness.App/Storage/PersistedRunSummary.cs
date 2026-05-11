namespace ArchHarness.App.Storage;

/// <summary>
/// Represents a persisted run discovered under a workspace run-history directory.
/// </summary>
/// <param name="RunId">The timestamp-based run identifier.</param>
/// <param name="RunDirectory">The full file-system path to the run directory.</param>
/// <param name="RunTitle">The human-friendly run title, when available.</param>
/// <param name="ProjectId">The stable project identifier associated with the run, when available.</param>
/// <param name="ProjectName">The project display name associated with the run, when available.</param>
public sealed record PersistedRunSummary(
    string RunId,
    string RunDirectory,
    string? RunTitle = null,
    string? ProjectId = null,
    string? ProjectName = null);
