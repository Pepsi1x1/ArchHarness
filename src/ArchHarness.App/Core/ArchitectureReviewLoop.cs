using ArchHarness.App.Agents;
using ArchHarness.App.Constants;
using ArchHarness.App.Storage;
using ArchHarness.App.Workspace;

namespace ArchHarness.App.Core;

/// <summary>
/// Owns the architecture review remediation iteration loop, invoking the Architecture agent
/// to re-review after remediation and enforcing the maximum iteration limit.
/// </summary>
public sealed class ArchitectureReviewLoop : IArchitectureReviewLoop
{
    /// <summary>
    /// Status string appended to required actions when consecutive review iterations produce identical findings, indicating no remediation progress.
    /// </summary>
    public const string NO_PROGRESS_BLOCKED_STATUS = "blocked:no-progress-identical-findings";
    private readonly AgentsOptions _agentsOptions;
    private readonly LoopAgentDependencies _agents;
    private readonly IRunStateStore _runStateStore;
    private readonly IRunContextAccessor _runContextAccessor;
    private readonly RuntimeStateAccessors _stateAccessors;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArchitectureReviewLoop"/> class.
    /// </summary>
    /// <param name="agents">Grouped agent references needed for the review loop.</param>
    /// <param name="agentsOptions">Agent configuration options.</param>
    /// <param name="stateAccessors">Grouped async-local runtime state accessors.</param>
    public ArchitectureReviewLoop(
        LoopAgentDependencies agents,
        Microsoft.Extensions.Options.IOptions<AgentsOptions> agentsOptions,
        IRunStateStore runStateStore,
        IRunContextAccessor runContextAccessor,
        RuntimeStateAccessors stateAccessors)
    {
        this._agentsOptions = agentsOptions.Value;
        this._agents = agents;
        this._runStateStore = runStateStore;
        this._runContextAccessor = runContextAccessor;
        this._stateAccessors = stateAccessors;
    }

    /// <summary>
    /// Runs the remediation iteration loop until no high-severity findings remain
    /// or the maximum iteration count is reached.
    /// </summary>
    /// <param name="iterationStrategy">Controls whether review is required and the max iterations.</param>
    /// <param name="initialReview">The architecture review from the initial pass.</param>
    /// <param name="filesTouched">Files modified during the build phase.</param>
    /// <param name="architectureLanguages">Language scope for the review.</param>
    /// <param name="request">The originating run request.</param>
    /// <param name="adapter">Workspace adapter for obtaining diffs.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Token to signal cancellation.</param>
    /// <returns>The final reviews and updated files-touched list.</returns>
    public async Task<(ArchitectureReview Review, SecurityReview SecurityReview, IReadOnlyList<string> FilesTouched)> RunAsync(
        ArchitectureLoopRequest request,
        IWorkspaceAdapter adapter,
        IProgress<RuntimeProgressEvent>? progress,
        CancellationToken cancellationToken)
    {
        ReviewLoopAgentSelection reviewLoopAgents = request.RunRequest.ReviewLoopAgents
            ?? this._stateAccessors.ReviewLoopAgentSelection.Current
            ?? this._agentsOptions.GetReviewLoopAgentSelection();
        ArchitectureReview review = request.InitialReview;
        SecurityReview securityReview = request.InitialSecurityReview;
        IReadOnlyList<string> currentFiles = request.RunRequest.ArchitectureLoopMode
            ? ArchitectureLoopHelpers.EnumerateWorkspaceFiles(adapter.RootPath, request.ArchitectureLanguages)
            : request.FilesTouched;
        int iteration = request.StartingIteration;
        string previousFindingsFingerprint = BuildFindingsFingerprint(review.Findings);
        string previousSecurityFindingsFingerprint = BuildSecurityFindingsFingerprint(securityReview.Findings);

        while (request.IterationStrategy.ReviewRequired &&
            (HasEnabledHighArchitectureFindings(reviewLoopAgents, review) || HasEnabledHighSecurityFindings(reviewLoopAgents, securityReview)) &&
            iteration < request.IterationStrategy.MaxIterations)
        {
            iteration++;
            progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, WellKnownSources.ARCHITECTURE_LOOP, $"Review iteration {iteration}"));

            string remediationPrompt = await this.BuildRemediationPromptAsync(request, adapter.RootPath, review, securityReview, iteration, cancellationToken);
            LoopIterationContext iterationContext = new LoopIterationContext(request, adapter, reviewLoopAgents, currentFiles, remediationPrompt, progress);

            string latestDiff = await adapter.DiffAsync(cancellationToken);
            await this.EnforceCodingStyleAsync(iterationContext, latestDiff, cancellationToken);
            securityReview = await this.RunSecurityReviewAsync(iterationContext, securityReview, cancellationToken);
            review = await this.RunArchitectureReviewAsync(iterationContext, review, cancellationToken);
            latestDiff = await adapter.DiffAsync(cancellationToken);

            string currentFindingsFingerprint = BuildFindingsFingerprint(review.Findings);
            string currentSecurityFindingsFingerprint = BuildSecurityFindingsFingerprint(securityReview.Findings);
            if (string.Equals(previousFindingsFingerprint, currentFindingsFingerprint, StringComparison.Ordinal)
                && string.Equals(previousSecurityFindingsFingerprint, currentSecurityFindingsFingerprint, StringComparison.Ordinal))
            {
                (review, securityReview) = ApplyBlockedStatus(reviewLoopAgents, review, securityReview);
                progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, WellKnownSources.ARCHITECTURE_LOOP, "Review blocked due to identical findings across iterations."));
                break;
            }

            previousFindingsFingerprint = currentFindingsFingerprint;
            previousSecurityFindingsFingerprint = currentSecurityFindingsFingerprint;
            currentFiles = request.RunRequest.ArchitectureLoopMode
                ? ArchitectureLoopHelpers.EnumerateWorkspaceFiles(adapter.RootPath, request.ArchitectureLanguages)
                : currentFiles
                    .Concat(ParseTouchedFiles(latestDiff))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

            await this.WriteLoopCheckpointAsync(
                adapter.RootPath,
                request,
                iteration,
                currentFiles,
                review,
                securityReview,
                cancellationToken);
        }

        return (review, securityReview, currentFiles);
    }

    private async Task<string> BuildRemediationPromptAsync(ArchitectureLoopRequest request, string workspaceRoot, ArchitectureReview review, SecurityReview securityReview, int iteration, CancellationToken cancellationToken)
    {
        string[] combinedRequiredActions = review.RequiredActions
            .Concat(securityReview.RequiredActions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return await this._agents.Orchestration.BuildRemediationPromptAsync(
            request.RunRequest,
            workspaceRoot,
            combinedRequiredActions,
            iteration,
            this._agents.Orchestration.Id,
            this._agents.Orchestration.Role,
            cancellationToken);
    }

    private async Task EnforceCodingStyleAsync(LoopIterationContext context, string latestDiff, CancellationToken cancellationToken)
    {
        if (!context.ReviewLoopAgents.CodingStyleEnabled)
        {
            return;
        }

        context.Progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, "CodingStyle", "Coding style enforcement prompt started", context.RemediationPrompt));
        string delegatedPrompt = context.Request.RunRequest.ArchitectureLoopMode
            ? ArchitectureLoopHelpers.BuildArchitectureLoopPrompt(context.RemediationPrompt, context.Request.RunRequest.ArchitectureLoopPrompt)
            : context.RemediationPrompt;
        await this._agents.CodingStyle.EnforceAsync(
            new StyleEnforcementRequest(
                DelegatedPrompt: delegatedPrompt,
                Diff: latestDiff,
                WorkspaceRoot: context.Adapter.RootPath,
                FilesTouched: context.CurrentFiles,
                LanguageScope: context.Request.ArchitectureLanguages,
                ModelOverrides: context.Request.RunRequest.ModelOverrides),
            this._agents.CodingStyle.Id,
            this._agents.CodingStyle.Role,
            cancellationToken);
    }

    private async Task<SecurityReview> RunSecurityReviewAsync(LoopIterationContext context, SecurityReview securityReview, CancellationToken cancellationToken)
    {
        if (!context.ReviewLoopAgents.SecurityEnabled)
        {
            return securityReview;
        }

        context.Progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, "Security", "Security enforcement prompt started", context.RemediationPrompt));
        string latestDiff = await context.Adapter.DiffAsync(cancellationToken);
        string delegatedPrompt = context.Request.RunRequest.ArchitectureLoopMode
            ? ArchitectureLoopHelpers.BuildArchitectureLoopPrompt(context.RemediationPrompt, context.Request.RunRequest.ArchitectureLoopPrompt)
            : context.RemediationPrompt;
        IReadOnlyList<string> securityFiles = context.Request.RunRequest.ArchitectureLoopMode
            ? ArchitectureLoopHelpers.EnumerateWorkspaceFiles(context.Adapter.RootPath, context.Request.SecurityLanguages)
            : context.CurrentFiles;
        return await this._agents.Security.ReviewAsync(
            new SecurityReviewRequest(
                DelegatedPrompt: delegatedPrompt,
                Diff: latestDiff,
                WorkspaceRoot: context.Adapter.RootPath,
                FilesTouched: securityFiles,
                LanguageScope: context.Request.SecurityLanguages,
                ModelOverrides: context.Request.RunRequest.ModelOverrides),
            this._agents.Security.Id,
            this._agents.Security.Role,
            cancellationToken);
    }

    private async Task<ArchitectureReview> RunArchitectureReviewAsync(LoopIterationContext context, ArchitectureReview review, CancellationToken cancellationToken)
    {
        if (!context.ReviewLoopAgents.ArchitectureEnabled)
        {
            return review;
        }

        context.Progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, "Architecture", "Enforcement prompt started", context.RemediationPrompt));
        string latestDiff = await context.Adapter.DiffAsync(cancellationToken);
        string delegatedPrompt = context.Request.RunRequest.ArchitectureLoopMode
            ? ArchitectureLoopHelpers.BuildArchitectureLoopPrompt(context.RemediationPrompt, context.Request.RunRequest.ArchitectureLoopPrompt)
            : context.RemediationPrompt;
        return await this._agents.Architecture.ReviewAsync(
            new ArchitectureReviewRequest(
                DelegatedPrompt: delegatedPrompt,
                Diff: latestDiff,
                WorkspaceRoot: context.Adapter.RootPath,
                FilesTouched: context.CurrentFiles,
                LanguageScope: context.Request.ArchitectureLanguages,
                ModelOverrides: context.Request.RunRequest.ModelOverrides),
            this._agents.Architecture.Id,
            this._agents.Architecture.Role,
            cancellationToken);
    }

    private static (ArchitectureReview Review, SecurityReview SecurityReview) ApplyBlockedStatus(ReviewLoopAgentSelection reviewLoopAgents, ArchitectureReview review, SecurityReview securityReview)
    {
        if (reviewLoopAgents.ArchitectureEnabled)
        {
            review = review with
            {
                RequiredActions = review.RequiredActions
                    .Concat(new[] { NO_PROGRESS_BLOCKED_STATUS })
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            };
        }

        if (reviewLoopAgents.SecurityEnabled)
        {
            securityReview = securityReview with
            {
                RequiredActions = securityReview.RequiredActions
                    .Concat(new[] { NO_PROGRESS_BLOCKED_STATUS })
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            };
        }

        return (review, securityReview);
    }

    private sealed record LoopIterationContext(
        ArchitectureLoopRequest Request,
        IWorkspaceAdapter Adapter,
        ReviewLoopAgentSelection ReviewLoopAgents,
        IReadOnlyList<string> CurrentFiles,
        string RemediationPrompt,
        IProgress<RuntimeProgressEvent>? Progress);

    private Task WriteLoopCheckpointAsync(
        string workspaceRoot,
        ArchitectureLoopRequest request,
        int iteration,
        IReadOnlyList<string> filesTouched,
        ArchitectureReview review,
        SecurityReview securityReview,
        CancellationToken cancellationToken)
    {
        RunContext? runContext = this._runContextAccessor.Current;
        if (runContext is null)
        {
            return Task.CompletedTask;
        }

        return this._runStateStore.UpdateStateAsync(
            runContext.RunDirectory,
            existingState => new PersistedRunState(
                runContext.RunId,
                runContext.RunDirectory,
                workspaceRoot,
                RunStatuses.RUNNING,
                RunPhases.ARCHITECTURE_LOOP,
                existingState?.StartedAtUtc ?? DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                request.RunRequest,
                existingState?.CompletedStepIds ?? Array.Empty<int>(),
                iteration,
                existingState?.FrontendPlan ?? string.Empty,
                filesTouched.ToArray(),
                review,
                securityReview,
                null),
            cancellationToken);
    }

    private static bool HasEnabledHighArchitectureFindings(ReviewLoopAgentSelection reviewLoopAgents, ArchitectureReview review)
        => reviewLoopAgents.ArchitectureEnabled
            && review.Findings.Any(f => f.Severity.Equals("high", StringComparison.OrdinalIgnoreCase));

    private static bool HasEnabledHighSecurityFindings(ReviewLoopAgentSelection reviewLoopAgents, SecurityReview review)
        => reviewLoopAgents.SecurityEnabled
            && review.Findings.Any(f => f.Severity.Equals("high", StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> ParseTouchedFiles(string? diff)
    {
        if (string.IsNullOrWhiteSpace(diff))
        {
            return Array.Empty<string>();
        }

        return diff
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildFindingsFingerprint(IReadOnlyList<ArchitectureFinding> findings)
        => BuildFingerprint(findings, f => $"{f.Severity}::{f.Rule}::{f.File}::{f.Symbol}::{f.Rationale}");

    private static string BuildSecurityFindingsFingerprint(IReadOnlyList<SecurityFinding> findings)
        => BuildFingerprint(findings, f => $"{f.Severity}::{f.Rule}::{f.File}::{f.Symbol}::{f.OwaspCategory}::{f.Rationale}");

    private static string BuildFingerprint<T>(IReadOnlyList<T> items, Func<T, string> keySelector)
    {
        if (items.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(
            "|",
            items
                .Select(keySelector)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Groups the agent references required for the architecture review loop, reducing constructor over-injection.
    /// </summary>
    public sealed class LoopAgentDependencies
    {
        /// <summary>Gets the orchestration agent.</summary>
        public OrchestrationAgent Orchestration { get; }
        /// <summary>Gets the coding style agent.</summary>
        public CodingStyleAgent CodingStyle { get; }
        /// <summary>Gets the security agent.</summary>
        public SecurityAgent Security { get; }
        /// <summary>Gets the architecture agent.</summary>
        public ArchitectureAgent Architecture { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="LoopAgentDependencies"/> class.
        /// </summary>
        /// <param name="orchestration">The orchestration agent.</param>
        /// <param name="codingStyle">The coding style agent.</param>
        /// <param name="security">The security agent.</param>
        /// <param name="architecture">The architecture agent.</param>
        public LoopAgentDependencies(
            OrchestrationAgent orchestration,
            CodingStyleAgent codingStyle,
            SecurityAgent security,
            ArchitectureAgent architecture)
        {
            this.Orchestration = orchestration;
            this.CodingStyle = codingStyle;
            this.Security = security;
            this.Architecture = architecture;
        }
    }
}
