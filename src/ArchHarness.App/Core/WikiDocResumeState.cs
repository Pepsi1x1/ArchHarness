using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArchHarness.App.Core;

/// <summary>
/// Captures the set of completed repository outputs from a previous WikiDoc run,
/// enabling the workflow to resume from where it stopped.
/// </summary>
public sealed record WikiDocResumeState(
    IReadOnlyList<WikiDocCompletedRepository> CompletedRepositories,
    bool MegaWikiCompleted);

/// <summary>
/// Records one repository that completed documentation in a prior WikiDoc run.
/// </summary>
public sealed record WikiDocCompletedRepository(
    string DocumentationSessionKey,
    string RepositoryRoot,
    string RepositoryRelativePath,
    string RepositoryDisplayName,
    string OutputRoot,
    string RequestedLocalRoot,
    string HomePath,
    bool UsedFallback,
    string? RenameCandidate,
    bool RenameCandidateWasEligible,
    string? RenamedFrom,
    string? FallbackReasonCode,
    string? FallbackReason,
    string Summary,
    IReadOnlyList<WikiDocConceptSeed> Concepts);

/// <summary>
/// Persisted checkpoint written after each repository completes, allowing resume
/// from <c>WikiDocCheckpoint.json</c> in the run directory.
/// </summary>
public sealed record WikiDocCheckpoint(
    [property: JsonPropertyName("completedRepositories")] IReadOnlyList<WikiDocCompletedRepository> CompletedRepositories,
    [property: JsonPropertyName("megaWikiCompleted")] bool MegaWikiCompleted,
    [property: JsonPropertyName("updatedAtUtc")] DateTimeOffset UpdatedAtUtc);
