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

    /// <summary>
    /// Resumes a prior WikiDoc run, skipping repositories that already completed.
    /// </summary>
    Task<WikiDocWorkflowResult> ExecuteAsync(RunRequest request, string runDirectory, WikiDocResumeState? resumeState, IProgress<RuntimeProgressEvent>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// Regenerates only the megawiki (Home.md and cross-repository concept pages) for a
    /// completed or partially-completed wikidoc run, reusing the per-repository outputs
    /// recorded in the run's <c>WikiDocCheckpoint.json</c>. Does not re-run any per-repository
    /// documentation agents.
    /// </summary>
    Task<WikiDocWorkflowResult> RegenerateAggregateAsync(RunRequest request, string runDirectory, IProgress<RuntimeProgressEvent>? progress, CancellationToken cancellationToken);
}

/// <summary>
/// Default implementation of <see cref="IWikiDocWorkflow"/>.
/// </summary>
public sealed class WikiDocWorkflow : IWikiDocWorkflow
{
    private const string HOME_FILE_NAME = "Home.md";
    private const string MARKDOWN_FILE_SEARCH_PATTERN = "*.md";
    private const string WIKIDOC_FALLBACK_DIRECTORY = "wikidoc-fallback";

    private readonly WikiDocAgent _agent;
    private readonly RuntimeStateAccessors _stateAccessors;
    private readonly WikiDocRepositoryDiscoverer _discoverer;
    private readonly WikiDocOutputResolver _resolver;
    private readonly IWikiDocMarkdownWriter _writer;
    private readonly IGlobalSettingsCatalog _settingsCatalog;
    private readonly SemaphoreSlim _checkpointLock = new SemaphoreSlim(1, 1);

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
    public Task<WikiDocWorkflowResult> ExecuteAsync(
        RunRequest request,
        string runDirectory,
        IProgress<RuntimeProgressEvent>? progress,
        CancellationToken cancellationToken)
        => this.ExecuteAsync(request, runDirectory, resumeState: null, progress, cancellationToken);

    /// <inheritdoc />
    public async Task<WikiDocWorkflowResult> ExecuteAsync(
        RunRequest request,
        string runDirectory,
        WikiDocResumeState? resumeState,
        IProgress<RuntimeProgressEvent>? progress,
        CancellationToken cancellationToken)
    {
        request = RunRequestWorkflowDefaults.Apply(request);
        string scanRoot = Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.WorkspacePath));
        IReadOnlyList<WikiDocRepositoryInfo> repositories = this._discoverer.Discover(scanRoot);
        ConcurrentBag<string> filesTouched = new ConcurrentBag<string>();
        ConcurrentBag<WikiDocRepositoryOutput> repositoryOutputs = new ConcurrentBag<WikiDocRepositoryOutput>();
        ConcurrentBag<WikiDocFallbackRecord> fallbackRecords = new ConcurrentBag<WikiDocFallbackRecord>();

        HashSet<string> completedSessionKeys = LoadResumeState(
            resumeState,
            repositories.Count,
            scanRoot,
            repositoryOutputs,
            filesTouched,
            progress);

        // Filter to only repositories that still need processing.
        IReadOnlyList<WikiDocRepositoryInfo> pendingRepositories = repositories
            .Where(r => !completedSessionKeys.Contains($"wikidoc-{r.SafeKey}"))
            .ToList();

        int totalRepositories = repositories.Count;
        int alreadyCompleted = completedSessionKeys.Count;
        int completedCount = alreadyCompleted;
        int parallelism = Math.Max(1, this._settingsCatalog.GetSettings().WikiDocParallelism);

        progress?.Report(new RuntimeProgressEvent(
            DateTimeOffset.UtcNow,
            WellKnownSources.WIKIDOC,
            $"wikidoc:progress:starting:{alreadyCompleted}/{totalRepositories}"));

        try
        {
            await Parallel.ForEachAsync(
                pendingRepositories,
                new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = cancellationToken },
                async (repository, ct) =>
            {
                string documentationSessionKey = $"wikidoc-{repository.SafeKey}";
                progress?.Report(new RuntimeProgressEvent(
                    DateTimeOffset.UtcNow,
                    WellKnownSources.WIKIDOC,
                    $"wikidoc:repo-started:{repository.DisplayName}:{Volatile.Read(ref completedCount)}/{totalRepositories}:{documentationSessionKey}"));

                WikiDocRepositoryOutput output = await this.DocumentRepositoryAsync(
                    repository,
                    scanRoot,
                    runDirectory,
                    request.ModelOverrides,
                    documentationSessionKey,
                    fallbackRecords,
                    ct).ConfigureAwait(false);
                repositoryOutputs.Add(output);
                AddMarkdownFiles(scanRoot, output.OutputRoot, filesTouched);

                int done = Interlocked.Increment(ref completedCount);
                progress?.Report(new RuntimeProgressEvent(
                    DateTimeOffset.UtcNow,
                    WellKnownSources.WIKIDOC,
                    $"wikidoc:repo-completed:{repository.DisplayName}:{done}/{totalRepositories}:{documentationSessionKey}"));

                // Write incremental checkpoint so progress survives crashes.
                await this.WriteCheckpointLockedAsync(runDirectory, repositoryOutputs.ToList(), megaWikiCompleted: false, ct).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        finally
        {
            // Ensure a checkpoint is persisted even when the loop exits due to error or cancellation,
            // so that already-completed repositories are not re-processed on resume.
            await PersistCheckpointBestEffortAsync(runDirectory, repositoryOutputs.ToList()).ConfigureAwait(false);
        }

        // Snapshot to lists for downstream consumption (order not significant).
        List<WikiDocRepositoryOutput> repositoryOutputsList = repositoryOutputs.ToList();
        List<WikiDocFallbackRecord> fallbackRecordsList = fallbackRecords.ToList();
        List<string> filesTouchedList = filesTouched.ToList();

        WikiDocAggregateOutput aggregateOutput = await this.GenerateAggregateOutputAsync(
            scanRoot,
            runDirectory,
            repositoryOutputsList,
            fallbackRecordsList,
            filesTouchedList,
            progress,
            cancellationToken).ConfigureAwait(false);

        await WriteCheckpointAsync(runDirectory, repositoryOutputsList, megaWikiCompleted: true, cancellationToken).ConfigureAwait(false);

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

    private static HashSet<string> LoadResumeState(
        WikiDocResumeState? resumeState,
        int repositoryCount,
        string scanRoot,
        ConcurrentBag<WikiDocRepositoryOutput> repositoryOutputs,
        ConcurrentBag<string> filesTouched,
        IProgress<RuntimeProgressEvent>? progress)
    {
        HashSet<string> completedSessionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (resumeState is null)
        {
            return completedSessionKeys;
        }

        progress?.Report(new RuntimeProgressEvent(
            DateTimeOffset.UtcNow,
            WellKnownSources.WIKIDOC,
            $"Resuming: {resumeState.CompletedRepositories.Count} repositories already completed, skipping."));

        foreach (WikiDocCompletedRepository completed in resumeState.CompletedRepositories)
        {
            completedSessionKeys.Add(completed.DocumentationSessionKey);
            repositoryOutputs.Add(ToRepositoryOutput(completed));
            AddMarkdownFiles(scanRoot, completed.OutputRoot, filesTouched);
        }

        progress?.Report(new RuntimeProgressEvent(
            DateTimeOffset.UtcNow,
            WellKnownSources.WIKIDOC,
            $"Resumed with {completedSessionKeys.Count} completed repositories, {repositoryCount - completedSessionKeys.Count} remaining"));
        return completedSessionKeys;
    }

    private static WikiDocRepositoryOutput ToRepositoryOutput(WikiDocCompletedRepository completed)
        => new WikiDocRepositoryOutput(
            completed.RepositoryRoot,
            completed.RepositoryRelativePath,
            completed.RepositoryDisplayName,
            completed.OutputRoot,
            completed.RequestedLocalRoot,
            completed.HomePath,
            completed.UsedFallback,
            completed.DocumentationSessionKey,
            completed.RenameCandidate,
            completed.RenameCandidateWasEligible,
            completed.RenamedFrom,
            completed.FallbackReasonCode,
            completed.FallbackReason,
            completed.Summary,
            completed.Concepts);

    private async Task<WikiDocRepositoryOutput> DocumentRepositoryAsync(
        WikiDocRepositoryInfo repository,
        string scanRoot,
        string runDirectory,
        IDictionary<string, string>? modelOverrides,
        string documentationSessionKey,
        ConcurrentBag<WikiDocFallbackRecord> fallbackRecords,
        CancellationToken cancellationToken)
    {
        string fallbackRoot = Path.Combine(runDirectory, WIKIDOC_FALLBACK_DIRECTORY, repository.SafeKey);
        WikiDocOutputResolution resolution = this._resolver.Resolve(repository.RepositoryRoot, fallbackRoot);
        WikiDocRepositoryIndex index = await this.WithWorkspaceRootAsync(
            repository.RepositoryRoot,
            () => this._agent.DocumentRepositoryAsync(
                scanRoot,
                repository,
                resolution.OutputRoot,
                modelOverrides,
                documentationSessionKey,
                cancellationToken)).ConfigureAwait(false);
        string homePath = await this.EnsureRepositoryHomeAsync(resolution.OutputRoot, index, cancellationToken).ConfigureAwait(false);
        RecordRepositoryFallback(repository.RepositoryRoot, resolution, fallbackRecords);

        return new WikiDocRepositoryOutput(
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
    }

    private async Task<string> EnsureRepositoryHomeAsync(
        string outputRoot,
        WikiDocRepositoryIndex index,
        CancellationToken cancellationToken)
    {
        string homePath = Path.Combine(outputRoot, HOME_FILE_NAME);
        if (File.Exists(homePath))
        {
            return homePath;
        }

        return await this._writer.WriteMarkdownAsync(
            outputRoot,
            HOME_FILE_NAME,
            WikiDocPathHelper.EnsureHeading($"# {index.RepositoryName}\n\n{index.Summary}", index.RepositoryName),
            cancellationToken).ConfigureAwait(false);
    }

    private static void RecordRepositoryFallback(
        string repositoryRoot,
        WikiDocOutputResolution resolution,
        ConcurrentBag<WikiDocFallbackRecord> fallbackRecords)
    {
        if (!resolution.UsedFallback)
        {
            return;
        }

        fallbackRecords.Add(new WikiDocFallbackRecord(
            "repository",
            repositoryRoot,
            resolution.RequestedLocalRoot,
            resolution.OutputRoot,
            resolution.FallbackReasonCode ?? "unknown",
            resolution.FallbackReason ?? "The repository-local wiki path could not be used."));
    }

    private static void AddMarkdownFiles(string scanRoot, string outputRoot, ConcurrentBag<string> filesTouched)
    {
        if (!Directory.Exists(outputRoot))
        {
            return;
        }

        foreach (string mdFile in Directory.GetFiles(outputRoot, MARKDOWN_FILE_SEARCH_PATTERN, SearchOption.AllDirectories))
        {
            filesTouched.Add(WikiDocPathHelper.ToWorkspaceRelativePath(scanRoot, mdFile));
        }
    }

    private static async Task PersistCheckpointBestEffortAsync(
        string runDirectory,
        IReadOnlyList<WikiDocRepositoryOutput> repositoryOutputs)
    {
        if (repositoryOutputs.Count == 0)
        {
            return;
        }

        try
        {
            await WriteCheckpointAsync(runDirectory, repositoryOutputs, megaWikiCompleted: false, CancellationToken.None).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // Best-effort: the incremental checkpoint from the last successful iteration may still exist on disk.
        }
    }

    /// <inheritdoc />
    public async Task<WikiDocWorkflowResult> RegenerateAggregateAsync(
        RunRequest request,
        string runDirectory,
        IProgress<RuntimeProgressEvent>? progress,
        CancellationToken cancellationToken)
    {
        request = RunRequestWorkflowDefaults.Apply(request);
        string scanRoot = Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.WorkspacePath));

        // Load the persisted per-repository outputs from the prior run.
        string checkpointPath = Path.Combine(runDirectory, "WikiDocCheckpoint.json");
        if (!File.Exists(checkpointPath))
        {
            throw new InvalidOperationException($"Cannot regenerate megawiki: checkpoint not found at '{checkpointPath}'.");
        }

        WikiDocCheckpoint? checkpoint;
        await using (FileStream checkpointStream = File.OpenRead(checkpointPath))
        {
            checkpoint = await JsonSerializer.DeserializeAsync<WikiDocCheckpoint>(
                checkpointStream,
                JsonDefaults.WEB_INDENTED,
                cancellationToken).ConfigureAwait(false);
        }

        if (checkpoint is null || checkpoint.CompletedRepositories.Count == 0)
        {
            throw new InvalidOperationException("Cannot regenerate megawiki: the checkpoint has no completed repositories.");
        }

        List<WikiDocRepositoryOutput> repositoryOutputs = checkpoint.CompletedRepositories
            .Select(completed => new WikiDocRepositoryOutput(
                completed.RepositoryRoot,
                completed.RepositoryRelativePath,
                completed.RepositoryDisplayName,
                completed.OutputRoot,
                completed.RequestedLocalRoot,
                completed.HomePath,
                completed.UsedFallback,
                completed.DocumentationSessionKey,
                completed.RenameCandidate,
                completed.RenameCandidateWasEligible,
                completed.RenamedFrom,
                completed.FallbackReasonCode,
                completed.FallbackReason,
                completed.Summary,
                completed.Concepts))
            .ToList();

        progress?.Report(new RuntimeProgressEvent(
            DateTimeOffset.UtcNow,
            WellKnownSources.WIKIDOC,
            $"wikidoc:megawiki-regenerate-started:{repositoryOutputs.Count}"));

        List<WikiDocFallbackRecord> fallbackRecords = new List<WikiDocFallbackRecord>();
        List<string> filesTouched = new List<string>();

        WikiDocAggregateOutput aggregateOutput = await this.GenerateAggregateOutputAsync(
            scanRoot,
            runDirectory,
            repositoryOutputs,
            fallbackRecords,
            filesTouched,
            progress,
            cancellationToken).ConfigureAwait(false);

        await WriteCheckpointAsync(runDirectory, repositoryOutputs, megaWikiCompleted: true, cancellationToken).ConfigureAwait(false);

        WikiDocExecutionReport report = new WikiDocExecutionReport(
            scanRoot,
            repositoryOutputs.Count,
            repositoryOutputs,
            aggregateOutput,
            fallbackRecords);

        await this._writer.WriteJsonAsync(Path.Combine(runDirectory, "WikiDocReport.json"), report, cancellationToken).ConfigureAwait(false);

        CompletionValidationResult validationResult = BuildValidationResult(report);
        string[] sortedFiles = filesTouched.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();

        progress?.Report(new RuntimeProgressEvent(
            DateTimeOffset.UtcNow,
            WellKnownSources.WIKIDOC,
            $"wikidoc:megawiki-regenerate-completed:{repositoryOutputs.Count}"));

        return new WikiDocWorkflowResult(sortedFiles, validationResult, report);
    }

    private async Task<WikiDocAggregateOutput> GenerateAggregateOutputAsync(
        string scanRoot,
        string runDirectory,
        IReadOnlyList<WikiDocRepositoryOutput> repositoryOutputs,
        List<WikiDocFallbackRecord> fallbackRecords,
        List<string> filesTouched,
        IProgress<RuntimeProgressEvent>? progress,
        CancellationToken cancellationToken)
    {
        string aggregateOwnerRoot = Path.Combine(scanRoot, "megawiki");
        string fallbackRoot = Path.Combine(runDirectory, WIKIDOC_FALLBACK_DIRECTORY, "megawiki");
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
                HOME_FILE_NAME,
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

        progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, WellKnownSources.WIKIDOC, $"wikidoc:megawiki-started:megawiki:{repositoryOutputs.Count}/{repositoryOutputs.Count}"));

        // ── Step 1: deterministically cluster concepts across all repositories ──
        // One cluster per concept name (case-insensitive). We already captured each repo's
        // concept summary during the per-repository pass, so the aggregate is a pure
        // stitching exercise — no second LLM round-trip per concept.
        List<ConceptCluster> clusters = BuildConceptClusters(repositoryOutputs);

        // ── Step 2: write one deterministic markdown page per concept ──
        // Intentionally no LLM call here: every per-concept call would cost a premium
        // request, and the prior pass already produced the summary text we would have
        // asked the model to paraphrase. With hundreds of repos this saves hundreds of
        // premium requests per run without losing information.
        string conceptsDir = Path.Combine(resolution.OutputRoot, "concepts");
        Directory.CreateDirectory(conceptsDir);
        List<string> writtenConceptPaths = new List<string>(clusters.Count);
        foreach (ConceptCluster cluster in clusters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string conceptFilePath = Path.Combine(conceptsDir, $"{cluster.Slug}.md");
            string markdown = BuildConceptFallbackMarkdown(cluster, conceptsDir);
            await File.WriteAllTextAsync(conceptFilePath, markdown, cancellationToken).ConfigureAwait(false);
            writtenConceptPaths.Add(conceptFilePath);
        }

        string[] conceptPaths = writtenConceptPaths
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (string conceptPath in conceptPaths)
        {
            filesTouched.Add(WikiDocPathHelper.ToWorkspaceRelativePath(scanRoot, conceptPath));
        }

        // ── Step 3: write megawiki Home.md deterministically from ALL repository outputs ──
        // Never delegate the repository index to an LLM: with hundreds of repositories the
        // one-shot prompt would truncate and only the first few repos would be rendered.
        string megaWikiPath = Path.Combine(resolution.OutputRoot, HOME_FILE_NAME);
        string homeMarkdown = BuildMegaWikiFallbackMarkdown(resolution.OutputRoot, repositoryOutputs, conceptPaths);
        await File.WriteAllTextAsync(megaWikiPath, homeMarkdown, cancellationToken).ConfigureAwait(false);
        filesTouched.Add(WikiDocPathHelper.ToWorkspaceRelativePath(scanRoot, megaWikiPath));

        progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, WellKnownSources.WIKIDOC, $"wikidoc:megawiki-completed:megawiki:{repositoryOutputs.Count}/{repositoryOutputs.Count}"));

        return new WikiDocAggregateOutput(
            resolution.OutputRoot,
            megaWikiPath,
            conceptPaths,
            resolution.UsedFallback,
            resolution.RenamedFrom,
            resolution.FallbackReasonCode,
            resolution.FallbackReason);
    }

    private sealed record ConceptCluster(
        string Name,
        string Slug,
        IReadOnlyList<WikiDocRepositoryOutput> ContributingRepositories,
        IReadOnlyDictionary<string, string> ConceptSummariesByRepo);

    private sealed record ConceptBucket(
        string CanonicalName,
        List<WikiDocRepositoryOutput> Repositories,
        Dictionary<string, string> Summaries);

    private static List<ConceptCluster> BuildConceptClusters(IReadOnlyList<WikiDocRepositoryOutput> repositoryOutputs)
    {
        Dictionary<string, ConceptBucket> byName = BuildConceptBuckets(repositoryOutputs);
        Dictionary<string, ConceptCluster> bySlug = new(StringComparer.OrdinalIgnoreCase);
        foreach (ConceptBucket bucket in byName.Values)
        {
            AddBucketToSlugIndex(bucket, bySlug);
        }

        return bySlug.Values
            .OrderBy(cluster => cluster.Slug, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Dictionary<string, ConceptBucket> BuildConceptBuckets(IReadOnlyList<WikiDocRepositoryOutput> repositoryOutputs)
    {
        Dictionary<string, ConceptBucket> byName = new(StringComparer.OrdinalIgnoreCase);

        foreach (WikiDocRepositoryOutput output in repositoryOutputs)
        {
            foreach (WikiDocConceptSeed concept in output.Concepts ?? Array.Empty<WikiDocConceptSeed>())
            {
                AddConceptToBucket(output, concept, byName);
            }
        }

        return byName;
    }

    private static void AddConceptToBucket(
        WikiDocRepositoryOutput output,
        WikiDocConceptSeed concept,
        Dictionary<string, ConceptBucket> byName)
    {
        if (string.IsNullOrWhiteSpace(concept.Name))
        {
            return;
        }

        string key = concept.Name.Trim();
        if (!byName.TryGetValue(key, out ConceptBucket? bucket))
        {
            bucket = new ConceptBucket(
                key,
                new List<WikiDocRepositoryOutput>(),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
            byName[key] = bucket;
        }

        AddRepositoryIfMissing(bucket.Repositories, output);
        bucket.Summaries[output.DocumentationSessionKey] = concept.Summary ?? string.Empty;
    }

    private static void AddBucketToSlugIndex(ConceptBucket bucket, Dictionary<string, ConceptCluster> bySlug)
    {
        string slug = SanitizeSlug(bucket.CanonicalName);
        if (string.IsNullOrWhiteSpace(slug))
        {
            return;
        }

        bySlug[slug] = bySlug.TryGetValue(slug, out ConceptCluster? existing)
            ? MergeConceptCluster(existing, bucket)
            : new ConceptCluster(bucket.CanonicalName, slug, bucket.Repositories, bucket.Summaries);
    }

    private static ConceptCluster MergeConceptCluster(ConceptCluster existing, ConceptBucket bucket)
    {
        List<WikiDocRepositoryOutput> mergedRepositories = existing.ContributingRepositories.ToList();
        mergedRepositories.AddRange(bucket.Repositories.Where(repo => !ContainsRepository(mergedRepositories, repo)));

        Dictionary<string, string> mergedSummaries = new(existing.ConceptSummariesByRepo, StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string> pair in bucket.Summaries)
        {
            mergedSummaries[pair.Key] = pair.Value;
        }

        return existing with
        {
            ContributingRepositories = mergedRepositories,
            ConceptSummariesByRepo = mergedSummaries
        };
    }

    private static void AddRepositoryIfMissing(List<WikiDocRepositoryOutput> repositories, WikiDocRepositoryOutput output)
    {
        if (!ContainsRepository(repositories, output))
        {
            repositories.Add(output);
        }
    }

    private static bool ContainsRepository(IReadOnlyList<WikiDocRepositoryOutput> repositories, WikiDocRepositoryOutput output)
        => repositories.Any(repository => string.Equals(
            repository.DocumentationSessionKey,
            output.DocumentationSessionKey,
            StringComparison.OrdinalIgnoreCase));

    private static string BuildConceptFallbackMarkdown(ConceptCluster cluster, string conceptsDir)
    {
        IEnumerable<string> lines = cluster.ContributingRepositories
            .OrderBy(repo => repo.RepositoryDisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(repo =>
            {
                string link = ToMarkdownRelativePath(conceptsDir, repo.HomePath);
                string summary = cluster.ConceptSummariesByRepo.TryGetValue(repo.DocumentationSessionKey, out string? s) && !string.IsNullOrWhiteSpace(s)
                    ? $": {s}"
                    : string.Empty;
                return $"- [{repo.RepositoryDisplayName}]({link}){summary}";
            });
        return $"# {cluster.Name}{Environment.NewLine}{Environment.NewLine}Contributing repositories:{Environment.NewLine}{Environment.NewLine}{string.Join(Environment.NewLine, lines)}{Environment.NewLine}";
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

    private async Task WriteCheckpointLockedAsync(
        string runDirectory,
        IReadOnlyList<WikiDocRepositoryOutput> completedOutputs,
        bool megaWikiCompleted,
        CancellationToken cancellationToken)
    {
        await this._checkpointLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteCheckpointAsync(runDirectory, completedOutputs, megaWikiCompleted, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            this._checkpointLock.Release();
        }
    }

    private static async Task WriteCheckpointAsync(
        string runDirectory,
        IReadOnlyList<WikiDocRepositoryOutput> completedOutputs,
        bool megaWikiCompleted,
        CancellationToken cancellationToken)
    {
        List<WikiDocCompletedRepository> completedRepos = completedOutputs.Select(output =>
            new WikiDocCompletedRepository(
                output.DocumentationSessionKey,
                output.RepositoryRoot,
                output.RepositoryRelativePath,
                output.RepositoryDisplayName,
                output.OutputRoot,
                output.RequestedLocalRoot,
                output.HomePath,
                output.UsedFallback,
                output.RenameCandidate,
                output.RenameCandidateWasEligible,
                output.RenamedFrom,
                output.FallbackReasonCode,
                output.FallbackReason,
                output.Summary,
                output.Concepts)).ToList();

        WikiDocCheckpoint checkpoint = new WikiDocCheckpoint(completedRepos, megaWikiCompleted, DateTimeOffset.UtcNow);
        string targetPath = Path.Combine(runDirectory, "WikiDocCheckpoint.json");
        string tempPath = targetPath + ".tmp";
        string json = JsonSerializer.Serialize(checkpoint, JsonDefaults.WEB_INDENTED);
        await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, targetPath, overwrite: true);
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

    private static string SanitizeSlug(string slug)
    {
        string normalized = new string(slug
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray());
        return string.Join("-", normalized.Split('-', StringSplitOptions.RemoveEmptyEntries));
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

