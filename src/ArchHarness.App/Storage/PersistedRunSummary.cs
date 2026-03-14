namespace ArchHarness.App.Storage;

/// <summary>
/// Represents a persisted run discovered under a workspace run-history directory.
/// </summary>
/// <param name="RunId">The timestamp-based run identifier.</param>
/// <param name="RunDirectory">The full file-system path to the run directory.</param>
public sealed record PersistedRunSummary(string RunId, string RunDirectory);