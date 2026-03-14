using ArchHarness.App.Agents;
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
    private readonly IReviewLoopAgentSelectionAccessor _reviewLoopAgentSelectionAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArchitectureReviewLoop"/> class.
    /// </summary>
    /// <param name="agents">Grouped agent references needed for the review loop.</param>
    public ArchitectureReviewLoop(LoopAgentDependencies agents, Microsoft.Extensions.Options.IOptions<AgentsOptions> agentsOptions, IReviewLoopAgentSelectionAccessor reviewLoopAgentSelectionAccessor)
    {
        this._agentsOptions = agentsOptions.Value;
        this._agents = agents;
        this._reviewLoopAgentSelectionAccessor = reviewLoopAgentSelectionAccessor;
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
            ?? this._reviewLoopAgentSelectionAccessor.Current
            ?? this._agentsOptions.GetReviewLoopAgentSelection();
        ArchitectureReview review = request.InitialReview;
        SecurityReview securityReview = request.InitialSecurityReview;
        IReadOnlyList<string> currentFiles = request.RunRequest.ArchitectureLoopMode
            ? ArchitectureLoopHelpers.EnumerateWorkspaceFiles(adapter.RootPath, request.ArchitectureLanguages)
            : request.FilesTouched;
        int iteration = 0;
        string previousFindingsFingerprint = BuildFindingsFingerprint(review.Findings);
        string previousSecurityFindingsFingerprint = BuildSecurityFindingsFingerprint(securityReview.Findings);

         while (request.IterationStrategy.ReviewRequired &&
             (HasEnabledHighArchitectureFindings(reviewLoopAgents, review) || HasEnabledHighSecurityFindings(reviewLoopAgents, securityReview)) &&
               iteration < request.IterationStrategy.MaxIterations)
        {
            iteration++;
            progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, "architecture-loop", $"Review iteration {iteration}"));

            string[] combinedRequiredActions = review.RequiredActions
                .Concat(securityReview.RequiredActions)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            string remediationPrompt = await this._agents.Orchestration.BuildRemediationPromptAsync(
                request.RunRequest,
                adapter.RootPath,
                combinedRequiredActions,
                iteration,
                this._agents.Orchestration.Id,
                this._agents.Orchestration.Role,
                cancellationToken);

            string latestDiff = await adapter.DiffAsync(cancellationToken);
            if (reviewLoopAgents.CodingStyleEnabled)
            {
                progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, "CodingStyle", "Coding style enforcement prompt started", remediationPrompt));
                await this._agents.CodingStyle.EnforceAsync(
                    new StyleEnforcementRequest(
                        DelegatedPrompt: remediationPrompt,
                        Diff: latestDiff,
                        WorkspaceRoot: adapter.RootPath,
                        FilesTouched: currentFiles,
                        LanguageScope: request.ArchitectureLanguages,
                        ModelOverrides: request.RunRequest.ModelOverrides),
                    this._agents.CodingStyle.Id,
                    this._agents.CodingStyle.Role,
                    cancellationToken);
            }

            if (reviewLoopAgents.SecurityEnabled)
            {
                progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, "Security", "Security enforcement prompt started", remediationPrompt));

                latestDiff = await adapter.DiffAsync(cancellationToken);
                string securityDelegatedPrompt = request.RunRequest.ArchitectureLoopMode
                    ? ArchitectureLoopHelpers.BuildArchitectureLoopPrompt(remediationPrompt, request.RunRequest.ArchitectureLoopPrompt)
                    : remediationPrompt;
                IReadOnlyList<string> securityFiles = request.RunRequest.ArchitectureLoopMode
                    ? ArchitectureLoopHelpers.EnumerateWorkspaceFiles(adapter.RootPath, request.SecurityLanguages)
                    : currentFiles;
                securityReview = await this._agents.Security.ReviewAsync(
                    new SecurityReviewRequest(
                        DelegatedPrompt: securityDelegatedPrompt,
                        Diff: latestDiff,
                        WorkspaceRoot: adapter.RootPath,
                        FilesTouched: securityFiles,
                        LanguageScope: request.SecurityLanguages,
                        ModelOverrides: request.RunRequest.ModelOverrides),
                    this._agents.Security.Id,
                    this._agents.Security.Role,
                    cancellationToken);
            }

            if (reviewLoopAgents.ArchitectureEnabled)
            {
                progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, "Architecture", "Enforcement prompt started", remediationPrompt));

                latestDiff = await adapter.DiffAsync(cancellationToken);
                string delegatedPrompt = request.RunRequest.ArchitectureLoopMode
                    ? ArchitectureLoopHelpers.BuildArchitectureLoopPrompt(remediationPrompt, request.RunRequest.ArchitectureLoopPrompt)
                    : remediationPrompt;
                review = await this._agents.Architecture.ReviewAsync(
                    new ArchitectureReviewRequest(
                        DelegatedPrompt: delegatedPrompt,
                        Diff: latestDiff,
                        WorkspaceRoot: adapter.RootPath,
                        FilesTouched: currentFiles,
                        LanguageScope: request.ArchitectureLanguages,
                        ModelOverrides: request.RunRequest.ModelOverrides),
                    this._agents.Architecture.Id,
                    this._agents.Architecture.Role,
                    cancellationToken);
            }

            string currentFindingsFingerprint = BuildFindingsFingerprint(review.Findings);
            string currentSecurityFindingsFingerprint = BuildSecurityFindingsFingerprint(securityReview.Findings);
            if (string.Equals(previousFindingsFingerprint, currentFindingsFingerprint, StringComparison.Ordinal)
                && string.Equals(previousSecurityFindingsFingerprint, currentSecurityFindingsFingerprint, StringComparison.Ordinal))
            {
                if (reviewLoopAgents.ArchitectureEnabled)
                {
                    string[] blockedActions = review.RequiredActions
                        .Concat(new[] { NO_PROGRESS_BLOCKED_STATUS })
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    review = review with { RequiredActions = blockedActions };
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
                progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, "architecture-loop", "Review blocked due to identical findings across iterations."));
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
        }

        return (review, securityReview, currentFiles);
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
    {
        if (findings.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(
            "|",
            findings
                .Select(f => $"{f.Severity}::{f.Rule}::{f.File}::{f.Symbol}::{f.Rationale}")
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
    }

    private static string BuildSecurityFindingsFingerprint(IReadOnlyList<SecurityFinding> findings)
    {
        if (findings.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(
            "|",
            findings
                .Select(f => $"{f.Severity}::{f.Rule}::{f.File}::{f.Symbol}::{f.OwaspCategory}::{f.Rationale}")
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
