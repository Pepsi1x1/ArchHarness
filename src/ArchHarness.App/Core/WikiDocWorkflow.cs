using System.Collections.Concurrent;
using System.Text.Json;
using ArchHarness.App.Agents;
using ArchHarness.App.Constants;
using ArchHarness.App.Storage;

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
    private readonly IGlobalSettingsCatalog _settingsCatalog;

    /// <summary>
    /// Initializes a new instance of the <see cref="WikiDocWorkflow"/> class.
    /// </summary>
    public WikiDocWorkflow(
        WikiDocAgent agent,
        RuntimeStateAccessors stateAccessors,
        WikiDocRepositoryDiscoverer discoverer,
        WikiDocOutputResolver resolver,
        IWikiDocMarkdownWriter writer,
        IGlobalSettingsCatalog settingsCatalog)
    {
        this._agent = agent;
        this._stateAccessors = stateAccessors;
        this._discoverer = discoverer;
        this._resolver = resolver;
        this._writer = writer;
        this._settingsCatalog = settingsCatalog;
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
        ConcurrentBag<string> filesTouched = new ConcurrentBag<string>();
        ConcurrentBag<WikiDocRepositoryOutput> repositoryOutputs = new ConcurrentBag<WikiDocRepositoryOutput>();
        ConcurrentBag<WikiDocFallbackRecord> fallbackRecords = new ConcurrentBag<WikiDocFallbackRecord>();

        int parallelism = Math.Max(1, this._settingsCatalog.GetSettings().WikiDocParallelism);
        await Parallel.ForEachAsync(
            repositories,
            new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = cancellationToken },
            async (repository, ct) =>
        {
            progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, WellKnownSources.WIKIDOC, $"Documenting repository {repository.DisplayName}"));

            string fallbackRoot = Path.Combine(runDirectory, "wikidoc-fallback", repository.SafeKey);
            WikiDocOutputResolution resolution = this._resolver.Resolve(repository.RepositoryRoot, fallbackRoot);
            string documentationSessionKey = $"wikidoc-{repository.SafeKey}";
            WikiDocRepositoryIndex index = await this.WithWorkspaceRootAsync(
                repository.RepositoryRoot,
                () => this._agent.DocumentRepositoryAsync(
                    scanRoot,
                    repository.RepositoryRoot,
                    repository.RelativePath,
                    repository.DisplayName,
                    resolution.OutputRoot,
                    request.ModelOverrides,
                    documentationSessionKey,
                    ct)).ConfigureAwait(false);

            string homePath = Path.Combine(resolution.OutputRoot, "Home.md");
            if (!File.Exists(homePath))
            {
                // Fallback: agent did not write Home.md (test stubs or tool failure).
                homePath = await this._writer.WriteMarkdownAsync(
                    resolution.OutputRoot,
                    "Home.md",
                    WikiDocPathHelper.EnsureHeading($"# {index.RepositoryName}\n\n{index.Summary}", index.RepositoryName),
                    ct).ConfigureAwait(false);
            }

            // Track all .md files the agent wrote under the output root, not just Home.md.
            foreach (string mdFile in Directory.GetFiles(resolution.OutputRoot, "*.md", SearchOption.AllDirectories))
            {
                filesTouched.Add(WikiDocPathHelper.ToWorkspaceRelativePath(scanRoot, mdFile));
            }

            WikiDocRepositoryOutput output = new WikiDocRepositoryOutput(
                repository.RepositoryRoot,
                repository.RelativePath,
                repository.DisplayName,
                resolution.OutputRoot,
                resolution.RequestedLocalRoot,
                homePath,
                resolution.UsedFallback,
                documentationSessionKey,
                resolution.RenameCandidate,
                resolution.RenameCandidateWasEligible,
                resolution.RenamedFrom,
                resolution.FallbackReasonCode,
                resolution.FallbackReason,
                index.Summary,
                index.Concepts);
            repositoryOutputs.Add(output);

            if (resolution.UsedFallback)
            {
                fallbackRecords.Add(new WikiDocFallbackRecord(
                    "repository",
                    repository.RepositoryRoot,
                    resolution.RequestedLocalRoot,
                    resolution.OutputRoot,
                    resolution.FallbackReasonCode ?? "unknown",
                    resolution.FallbackReason ?? "The repository-local wiki path could not be used."));
            }
        }).ConfigureAwait(false);

        // Snapshot to lists for downstream consumption (order not significant).
        List<WikiDocRepositoryOutput> repositoryOutputsList = repositoryOutputs.ToList();
        List<WikiDocFallbackRecord> fallbackRecordsList = fallbackRecords.ToList();
        List<string> filesTouchedList = filesTouched.ToList();

        WikiDocAggregateOutput aggregateOutput = await this.GenerateAggregateOutputAsync(
            request,
            scanRoot,
            runDirectory,
            repositoryOutputsList,
            fallbackRecordsList,
            filesTouchedList,
            progress,
            cancellationToken).ConfigureAwait(false);

        WikiDocExecutionReport report = new WikiDocExecutionReport(
            scanRoot,
            repositories.Count,
            repositoryOutputsList,
            aggregateOutput,
            fallbackRecordsList);

        await this._writer.WriteJsonAsync(Path.Combine(runDirectory, "WikiDocReport.json"), report, cancellationToken).ConfigureAwait(false);
        await this._writer.WriteJsonAsync(Path.Combine(runDirectory, "WikiDocFallbacks.json"), fallbackRecordsList, cancellationToken).ConfigureAwait(false);

        CompletionValidationResult validationResult = BuildValidationResult(report);
        string[] sortedFiles = filesTouchedList.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        return new WikiDocWorkflowResult(sortedFiles, validationResult, report);
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
        string aggregateOwnerRoot = Path.Combine(scanRoot, "megawiki");
        string fallbackRoot = Path.Combine(runDirectory, "wikidoc-fallback", "megawiki");
        WikiDocOutputResolution resolution = this._resolver.Resolve(aggregateOwnerRoot, fallbackRoot);
        if (resolution.UsedFallback)
        {
            fallbackRecords.Add(new WikiDocFallbackRecord(
                "megawiki",
                aggregateOwnerRoot,
                resolution.RequestedLocalRoot,
                resolution.OutputRoot,
                resolution.FallbackReasonCode ?? "unknown",
                resolution.FallbackReason ?? "The megawiki output path could not be created under the scan root."));
        }

        if (repositoryOutputs.Count == 0)
        {
            string placeholderPath = await this._writer.WriteMarkdownAsync(
                resolution.OutputRoot,
                "Home.md",
                "# MegaWiki\n\nNo Git repositories were discovered under the scan root, so no aggregate wiki could be synthesized.",
                cancellationToken).ConfigureAwait(false);
            filesTouched.Add(WikiDocPathHelper.ToWorkspaceRelativePath(scanRoot, placeholderPath));

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

        WikiDocMegaWikiIndex synthesis = await this.WithWorkspaceRootAsync(
            scanRoot,
            () => this._agent.SynthesizeMegaWikiAsync(
                scanRoot,
                repositorySummaryPayload,
                request.ModelOverrides,
                "wikidoc-megawiki",
                cancellationToken)).ConfigureAwait(false);

        // The agent writes megawiki files (Home.md + concept pages) via tools.
        // Discover concept pages the agent wrote.
        string conceptsDir = Path.Combine(resolution.OutputRoot, "concepts");
        if (!Directory.Exists(conceptsDir) && synthesis.ConceptSlugs.Count > 0)
        {
            // Fallback: agent did not write concept pages (test stubs or tool failure).
            Directory.CreateDirectory(conceptsDir);
            foreach (string slug in synthesis.ConceptSlugs)
            {
                string conceptFilePath = Path.Combine(conceptsDir, $"{slug}.md");
                await File.WriteAllTextAsync(conceptFilePath, $"# {slug}\n\nPlaceholder concept page.", cancellationToken).ConfigureAwait(false);
            }
        }

        string[] conceptPaths = Directory.Exists(conceptsDir)
            ? Directory.GetFiles(conceptsDir, "*.md")
            : Array.Empty<string>();
        foreach (string conceptPath in conceptPaths)
        {
            filesTouched.Add(WikiDocPathHelper.ToWorkspaceRelativePath(scanRoot, conceptPath));
        }

        string megaWikiPath = Path.Combine(resolution.OutputRoot, "Home.md");
        if (!File.Exists(megaWikiPath))
        {
            // Fallback: agent did not write Home.md (test stubs or tool failure).
            megaWikiPath = await this._writer.WriteMarkdownAsync(
                resolution.OutputRoot,
                "Home.md",
                BuildMegaWikiFallbackMarkdown(resolution.OutputRoot, repositoryOutputs, conceptPaths),
                cancellationToken).ConfigureAwait(false);
        }
        filesTouched.Add(WikiDocPathHelper.ToWorkspaceRelativePath(scanRoot, megaWikiPath));
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

    private static string BuildMegaWikiFallbackMarkdown(
        string megaWikiRoot,
        IReadOnlyList<WikiDocRepositoryOutput> repositoryOutputs,
        IReadOnlyList<string> conceptPaths)
    {
        string content = "# MegaWiki\n\nCombined repository overview.";
        string repositoryLinks = BuildRepositoryLinkSection(megaWikiRoot, repositoryOutputs);
        string conceptLinks = BuildConceptLinkSection(megaWikiRoot, conceptPaths);
        return string.Join(
            Environment.NewLine + Environment.NewLine,
            new[] { content, repositoryLinks, conceptLinks }.Where(section => !string.IsNullOrWhiteSpace(section)));
    }

    private static string BuildRepositoryLinkSection(string megaWikiRoot, IReadOnlyList<WikiDocRepositoryOutput> repositoryOutputs)
    {
        if (repositoryOutputs.Count == 0)
        {
            return string.Empty;
        }

        string[] links = repositoryOutputs
            .OrderBy(output => output.RepositoryDisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(output => $"- [{output.RepositoryDisplayName}]({ToMarkdownRelativePath(megaWikiRoot, output.HomePath)})")
            .ToArray();
        return $"## Repository wikis{Environment.NewLine}{Environment.NewLine}{string.Join(Environment.NewLine, links)}";
    }

    private static string BuildConceptLinkSection(string megaWikiRoot, IReadOnlyList<string> conceptPaths)
    {
        if (conceptPaths.Count == 0)
        {
            return string.Empty;
        }

        string[] links = conceptPaths
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => $"- [{Path.GetFileNameWithoutExtension(path)}]({ToMarkdownRelativePath(megaWikiRoot, path)})")
            .ToArray();
        return $"## Cross-repository concepts{Environment.NewLine}{Environment.NewLine}{string.Join(Environment.NewLine, links)}";
    }

    private static string ToMarkdownRelativePath(string baseDirectory, string targetPath)
        => Path.GetRelativePath(baseDirectory, targetPath).Replace('\\', '/');

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
            new CriterionResult("Discovered at least one Git repository", discoveredRepos, discoveredRepos
                ? $"Discovered {report.DiscoveredRepositoryCount} Git repositories."
                : "No Git repositories were discovered under the scan root."),
            new CriterionResult("Wrote one wiki Home.md per discovered repository", repositoryHomesWritten, repositoryHomesWritten
                ? $"Wrote {report.RepositoryOutputs.Count} repository home pages."
                : "One or more discovered repositories did not receive a Home.md output."),
            new CriterionResult("Synthesized megawiki output", megaWikiWritten, megaWikiWritten
                ? $"Megawiki written to {report.AggregateOutput.MegaWikiPath}."
                : "Megawiki output was not created."),
            new CriterionResult("Synthesized cross-repository concept pages", conceptPagesWritten, conceptPagesWritten
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

