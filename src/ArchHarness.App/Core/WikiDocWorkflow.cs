using System.Text.Json;
using ArchHarness.App.Agents;
using ArchHarness.App.Constants;

namespace ArchHarness.App.Core;

/// <summary>
/// Executes the wikidoc workflow over one or more discovered Git repositories.
/// </summary>
public interface IWikiDocWorkflow
{
    /// <summary>
    /// Executes wiki documentation generation for the supplied run request.
    /// </summary>
    Task<WikiDocWorkflowResult> ExecuteAsync(RunRequest request, string runDirectory, IProgress<RuntimeProgressEvent>? progress, CancellationToken cancellationToken);
}

/// <summary>
/// Default implementation of <see cref="IWikiDocWorkflow"/>.
/// </summary>
public sealed class WikiDocWorkflow : IWikiDocWorkflow
{
    private readonly WikiDocAgent _agent;
    private readonly RuntimeStateAccessors _stateAccessors;
    private readonly WikiDocRepositoryDiscoverer _discoverer;
    private readonly WikiDocOutputResolver _resolver;
    private readonly IWikiDocMarkdownWriter _writer;

    /// <summary>
    /// Initializes a new instance of the <see cref="WikiDocWorkflow"/> class.
    /// </summary>
    public WikiDocWorkflow(
        WikiDocAgent agent,
        RuntimeStateAccessors stateAccessors,
        WikiDocRepositoryDiscoverer discoverer,
        WikiDocOutputResolver resolver,
        IWikiDocMarkdownWriter writer)
    {
        this._agent = agent;
        this._stateAccessors = stateAccessors;
        this._discoverer = discoverer;
        this._resolver = resolver;
        this._writer = writer;
    }

    /// <inheritdoc />
    public async Task<WikiDocWorkflowResult> ExecuteAsync(
        RunRequest request,
        string runDirectory,
        IProgress<RuntimeProgressEvent>? progress,
        CancellationToken cancellationToken)
    {
        request = RunRequestWorkflowDefaults.Apply(request);
        string scanRoot = Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.WorkspacePath));
        IReadOnlyList<WikiDocRepositoryInfo> repositories = this._discoverer.Discover(scanRoot);
        List<string> filesTouched = new List<string>();
        List<WikiDocRepositoryOutput> repositoryOutputs = new List<WikiDocRepositoryOutput>();
        List<WikiDocFallbackRecord> fallbackRecords = new List<WikiDocFallbackRecord>();

        foreach (WikiDocRepositoryInfo repository in repositories)
        {
            progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, WellKnownSources.WIKIDOC, $"Documenting repository {repository.DisplayName}"));

            string fallbackRoot = Path.Combine(runDirectory, "wikidoc-fallback", repository.SafeKey);
            WikiDocOutputResolution resolution = this._resolver.Resolve(repository.RepositoryRoot, fallbackRoot);
            WikiDocRepositoryDocument document = await this.WithWorkspaceRootAsync(
                repository.RepositoryRoot,
                () => this._agent.DocumentRepositoryAsync(
                    scanRoot,
                    repository.RepositoryRoot,
                    repository.RelativePath,
                    repository.DisplayName,
                    resolution.OutputRoot,
                    request.ModelOverrides,
                    $"wikidoc-{repository.SafeKey}",
                    cancellationToken)).ConfigureAwait(false);

            string homePath = await this._writer.WriteMarkdownAsync(
                resolution.OutputRoot,
                "Home.md",
                this._writer.EnsureHeading(document.HomeMarkdown, document.RepositoryName),
                cancellationToken).ConfigureAwait(false);
            filesTouched.Add(this._writer.ToWorkspaceRelativePath(scanRoot, homePath));

            WikiDocRepositoryOutput output = new WikiDocRepositoryOutput(
                repository.RepositoryRoot,
                repository.RelativePath,
                repository.DisplayName,
                resolution.OutputRoot,
                homePath,
                resolution.UsedFallback,
                resolution.RenamedFrom,
                resolution.FallbackReasonCode,
                resolution.FallbackReason,
                document.Summary,
                document.Concepts);
            repositoryOutputs.Add(output);

            if (resolution.UsedFallback)
            {
                fallbackRecords.Add(new WikiDocFallbackRecord(
                    "repository",
                    repository.RepositoryRoot,
                    Path.Combine(repository.RepositoryRoot, "wiki"),
                    resolution.OutputRoot,
                    resolution.FallbackReasonCode ?? "unknown",
                    resolution.FallbackReason ?? "The repository-local wiki path could not be used."));
            }
        }

        WikiDocAggregateOutput aggregateOutput = await this.GenerateAggregateOutputAsync(
            request,
            scanRoot,
            runDirectory,
            repositoryOutputs,
            fallbackRecords,
            filesTouched,
            progress,
            cancellationToken).ConfigureAwait(false);

        WikiDocExecutionReport report = new WikiDocExecutionReport(
            scanRoot,
            repositories.Count,
            repositoryOutputs,
            aggregateOutput,
            fallbackRecords);

        await this._writer.WriteJsonAsync(Path.Combine(runDirectory, "WikiDocReport.json"), report, cancellationToken).ConfigureAwait(false);
        await this._writer.WriteJsonAsync(Path.Combine(runDirectory, "WikiDocFallbacks.json"), fallbackRecords, cancellationToken).ConfigureAwait(false);

        CompletionValidationResult validationResult = BuildValidationResult(report);
        return new WikiDocWorkflowResult(filesTouched.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(), validationResult, report);
    }

    private async Task<WikiDocAggregateOutput> GenerateAggregateOutputAsync(
        RunRequest request,
        string scanRoot,
        string runDirectory,
        IReadOnlyList<WikiDocRepositoryOutput> repositoryOutputs,
        List<WikiDocFallbackRecord> fallbackRecords,
        List<string> filesTouched,
        IProgress<RuntimeProgressEvent>? progress,
        CancellationToken cancellationToken)
    {
        string fallbackRoot = Path.Combine(runDirectory, "wikidoc-fallback", "megawiki");
        WikiDocOutputResolution resolution = this._resolver.Resolve(scanRoot, fallbackRoot);
        if (resolution.UsedFallback)
        {
            fallbackRecords.Add(new WikiDocFallbackRecord(
                "megawiki",
                scanRoot,
                Path.Combine(scanRoot, "wiki"),
                resolution.OutputRoot,
                resolution.FallbackReasonCode ?? "unknown",
                resolution.FallbackReason ?? "The megawiki output path could not be created under the scan root."));
        }

        if (repositoryOutputs.Count == 0)
        {
            string placeholderPath = await this._writer.WriteMarkdownAsync(
                resolution.OutputRoot,
                "MegaWiki.md",
                "# MegaWiki\n\nNo Git repositories were discovered under the scan root, so no aggregate wiki could be synthesized.",
                cancellationToken).ConfigureAwait(false);
            filesTouched.Add(this._writer.ToWorkspaceRelativePath(scanRoot, placeholderPath));

            return new WikiDocAggregateOutput(
                resolution.OutputRoot,
                placeholderPath,
                Array.Empty<string>(),
                resolution.UsedFallback,
                resolution.RenamedFrom,
                resolution.FallbackReasonCode,
                resolution.FallbackReason);
        }

        progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, WellKnownSources.WIKIDOC, "Synthesizing megawiki and concept pages"));
        string repositorySummaryPayload = JsonSerializer.Serialize(
            repositoryOutputs.Select(output => new
            {
                output.RepositoryRelativePath,
                output.RepositoryDisplayName,
                output.HomePath,
                output.Summary,
                Concepts = output.Concepts.Select(concept => new { concept.Name, concept.Summary })
            }),
            JsonDefaults.INDENTED);

        WikiDocMegaWikiDocument synthesis = await this.WithWorkspaceRootAsync(
            scanRoot,
            () => this._agent.SynthesizeMegaWikiAsync(
                scanRoot,
                repositorySummaryPayload,
                request.ModelOverrides,
                "wikidoc-megawiki",
                cancellationToken)).ConfigureAwait(false);

        string megaWikiPath = await this._writer.WriteMarkdownAsync(
            resolution.OutputRoot,
            "MegaWiki.md",
            this._writer.EnsureHeading(synthesis.MegaWikiMarkdown, "MegaWiki"),
            cancellationToken).ConfigureAwait(false);
        filesTouched.Add(this._writer.ToWorkspaceRelativePath(scanRoot, megaWikiPath));

        string[] conceptPaths = await this._writer.WriteConceptPagesAsync(scanRoot, resolution.OutputRoot, synthesis.ConceptPages, filesTouched, cancellationToken).ConfigureAwait(false);
        return new WikiDocAggregateOutput(
            resolution.OutputRoot,
            megaWikiPath,
            conceptPaths,
            resolution.UsedFallback,
            resolution.RenamedFrom,
            resolution.FallbackReasonCode,
            resolution.FallbackReason);
    }

    private async Task<T> WithWorkspaceRootAsync<T>(string workspaceRoot, Func<Task<T>> action)
    {
        string? previous = this._stateAccessors.WorkspaceRoot.Current;
        this._stateAccessors.WorkspaceRoot.SetCurrent(workspaceRoot);
        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            this._stateAccessors.WorkspaceRoot.SetCurrent(previous);
        }
    }

    private static CompletionValidationResult BuildValidationResult(WikiDocExecutionReport report)
    {
        bool discoveredRepos = report.DiscoveredRepositoryCount > 0;
        bool repositoryHomesWritten = discoveredRepos
            && report.RepositoryOutputs.All(output => File.Exists(output.HomePath));
        bool megaWikiWritten = File.Exists(report.AggregateOutput.MegaWikiPath);
        bool conceptPagesWritten = report.AggregateOutput.ConceptPagePaths.Count > 0
            && report.AggregateOutput.ConceptPagePaths.All(File.Exists);

        CriterionResult[] criteria =
        {
            new("Discovered at least one Git repository", discoveredRepos, discoveredRepos
                ? $"Discovered {report.DiscoveredRepositoryCount} Git repositories."
                : "No Git repositories were discovered under the scan root."),
            new("Wrote one wiki Home.md per discovered repository", repositoryHomesWritten, repositoryHomesWritten
                ? $"Wrote {report.RepositoryOutputs.Count} repository home pages."
                : "One or more discovered repositories did not receive a Home.md output."),
            new("Synthesized megawiki output", megaWikiWritten, megaWikiWritten
                ? $"Megawiki written to {report.AggregateOutput.MegaWikiPath}."
                : "Megawiki output was not created."),
            new("Synthesized cross-repository concept pages", conceptPagesWritten, conceptPagesWritten
                ? $"Wrote {report.AggregateOutput.ConceptPagePaths.Count} concept pages."
                : "No concept pages were written.")
        };

        bool passed = criteria.All(result => result.Passed);
        string summary = passed
            ? $"Wiki documentation generated for {report.RepositoryOutputs.Count} repositories."
            : "Wiki documentation workflow completed with missing outputs.";
        ImplementationAssessment assessment = new ImplementationAssessment(
            passed ? "PASS" : "FAIL",
            passed,
            summary,
            criteria.Where(result => result.Passed).Select(result => result.Evidence).ToArray(),
            criteria.Where(result => !result.Passed).Select(result => result.Evidence).ToArray(),
            report.Fallbacks.Count == 0
                ? Array.Empty<string>()
                : report.Fallbacks.Select(fallback => $"{fallback.Scope}: {fallback.ReasonCode}").ToArray());

        return new CompletionValidationResult(
            passed,
            criteria,
            summary,
            passed ? "high" : "medium",
            Assessment: assessment);
    }
}

/// <summary>
/// Structured response returned by the repository documentation pass.
/// </summary>
/// <param name="RepositoryName">Repository display name.</param>
/// <param name="Summary">Concise repository summary.</param>
/// <param name="HomeMarkdown">Full Home.md markdown.</param>
/// <param name="Concepts">Concept seeds for cross-repository synthesis.</param>
public sealed record WikiDocRepositoryDocument(
    string RepositoryName,
    string Summary,
    string HomeMarkdown,
    IReadOnlyList<WikiDocConceptSeed> Concepts);

/// <summary>
/// Cross-repository concept seed extracted from a repository pass.
/// </summary>
public sealed record WikiDocConceptSeed(string Name, string Summary);

/// <summary>
/// Structured response returned by the megawiki synthesis pass.
/// </summary>
public sealed record WikiDocMegaWikiDocument(
    string MegaWikiMarkdown,
    IReadOnlyList<WikiDocConceptPage> ConceptPages);

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
    string HomePath,
    bool UsedFallback,
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

public sealed record WikiDocRepositoryInfo(
    string RepositoryRoot,
    string RelativePath,
    string DisplayName,
    string SafeKey);

public sealed record WikiDocOutputResolution(
    string OutputRoot,
    bool UsedFallback,
    string? RenamedFrom,
    string? FallbackReasonCode,
    string? FallbackReason);
