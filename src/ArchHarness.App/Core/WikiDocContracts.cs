namespace ArchHarness.App.Core;

/// <summary>
/// Structured index returned by the repository documentation pass.
/// The agent writes wiki content directly via tools; only the index is returned.
/// </summary>
/// <param name="RepositoryName">Repository display name.</param>
/// <param name="Summary">Concise repository summary.</param>
/// <param name="Pages">List of .md filenames the agent wrote (e.g. Home.md, Architecture.md).</param>
/// <param name="Concepts">Concept seeds for cross-repository synthesis.</param>
public sealed record WikiDocRepositoryIndex(
    string RepositoryName,
    string Summary,
    IReadOnlyList<string> Pages,
    IReadOnlyList<WikiDocConceptSeed> Concepts);

/// <summary>
/// Cross-repository concept seed extracted from a repository pass.
/// </summary>
public sealed record WikiDocConceptSeed(string Name, string Summary);

/// <summary>
/// Structured index returned by the megawiki synthesis pass.
/// The agent writes megawiki markdown and concept pages directly via tools; only the manifest is returned.
/// </summary>
public sealed record WikiDocMegaWikiIndex(
    IReadOnlyList<string> ConceptSlugs);

/// <summary>
/// Represents a synthesized shared concept page.
/// </summary>
public sealed record WikiDocConceptPage(string Slug, string Title, string Markdown);

/// <summary>
/// Final wikidoc workflow result consumed by the run processor.
/// </summary>
public sealed record WikiDocWorkflowResult(
    IReadOnlyList<string> FilesTouched,
    CompletionValidationResult ValidationResult,
    WikiDocExecutionReport Report);

/// <summary>
/// Aggregated run report for wikidoc output generation.
/// </summary>
public sealed record WikiDocExecutionReport(
    string ScanRoot,
    int DiscoveredRepositoryCount,
    IReadOnlyList<WikiDocRepositoryOutput> RepositoryOutputs,
    WikiDocAggregateOutput AggregateOutput,
    IReadOnlyList<WikiDocFallbackRecord> Fallbacks);

/// <summary>
/// Records one repository-local documentation output.
/// </summary>
public sealed record WikiDocRepositoryOutput(
    string RepositoryRoot,
    string RepositoryRelativePath,
    string RepositoryDisplayName,
    string OutputRoot,
    string RequestedLocalRoot,
    string HomePath,
    bool UsedFallback,
    string DocumentationSessionKey,
    string? RenameCandidate,
    bool RenameCandidateWasEligible,
    string? RenamedFrom,
    string? FallbackReasonCode,
    string? FallbackReason,
    string Summary,
    IReadOnlyList<WikiDocConceptSeed> Concepts);

/// <summary>
/// Records megawiki output paths.
/// </summary>
public sealed record WikiDocAggregateOutput(
    string OutputRoot,
    string MegaWikiPath,
    IReadOnlyList<string> ConceptPagePaths,
    bool UsedFallback,
    string? RenamedFrom,
    string? FallbackReasonCode,
    string? FallbackReason);

/// <summary>
/// Explicit record of a deterministic fallback output.
/// </summary>
public sealed record WikiDocFallbackRecord(
    string Scope,
    string OwnerRoot,
    string RequestedLocalRoot,
    string FallbackRoot,
    string ReasonCode,
    string Reason);

/// <summary>
/// Identifies a discovered Git repository with its scan-relative path and filesystem-safe key.
/// </summary>
public sealed record WikiDocRepositoryInfo(
    string RepositoryRoot,
    string RelativePath,
    string DisplayName,
    string SafeKey);

/// <summary>
/// Describes the resolved wiki output root and any rename or fallback that was applied.
/// </summary>
public sealed record WikiDocOutputResolution(
    string OutputRoot,
    string RequestedLocalRoot,
    bool UsedFallback,
    string? RenameCandidate,
    bool RenameCandidateWasEligible,
    string? RenamedFrom,
    string? FallbackReasonCode,
    string? FallbackReason);
