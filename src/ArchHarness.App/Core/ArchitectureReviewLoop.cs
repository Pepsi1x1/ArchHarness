using ArchHarness.App.Agents;
using ArchHarness.App.Workspace;

namespace ArchHarness.App.Core;

/// <summary>
/// Owns the architecture review remediation iteration loop, invoking the Architecture agent
/// to re-review after remediation and enforcing the maximum iteration limit.
/// </summary>
public sealed class ArchitectureReviewLoop
{
    public const string NO_PROGRESS_BLOCKED_STATUS = "blocked:no-progress-identical-findings";
    private readonly OrchestrationAgent _orchestrationAgent;
    private readonly StyleAgent _styleAgent;
    private readonly ArchitectureAgent _architectureAgent;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArchitectureReviewLoop"/> class.
    /// </summary>
    /// <param name="orchestrationAgent">Agent used to build remediation prompts.</param>
    /// <param name="styleAgent">Agent used to enforce coding style before architecture review.</param>
    /// <param name="architectureAgent">Agent used to perform architecture reviews.</param>
    public ArchitectureReviewLoop(
        OrchestrationAgent orchestrationAgent,
        StyleAgent styleAgent,
        ArchitectureAgent architectureAgent)
    {
        this._orchestrationAgent = orchestrationAgent;
        this._styleAgent = styleAgent;
        this._architectureAgent = architectureAgent;
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
    /// <returns>The final review and updated files-touched list.</returns>
    public async Task<(ArchitectureReview Review, IReadOnlyList<string> FilesTouched)> RunAsync(
        ArchitectureLoopRequest request,
        IWorkspaceAdapter adapter,
        IProgress<RuntimeProgressEvent>? progress,
        CancellationToken cancellationToken)
    {
        ArchitectureReview review = request.InitialReview;
        IReadOnlyList<string> currentFiles = request.RunRequest.ArchitectureLoopMode
            ? ArchitectureLoopHelpers.EnumerateWorkspaceFiles(adapter.RootPath, request.ArchitectureLanguages)
            : request.FilesTouched;
        int iteration = 0;
        string previousFindingsFingerprint = BuildFindingsFingerprint(review.Findings);

        while (request.IterationStrategy.ReviewRequired &&
               review.Findings.Any(f => f.Severity.Equals("high", StringComparison.OrdinalIgnoreCase)) &&
               iteration < request.IterationStrategy.MaxIterations)
        {
            iteration++;
            progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, "architecture-loop", $"Review iteration {iteration}"));

            string remediationPrompt = await this._orchestrationAgent.BuildRemediationPromptAsync(
                request.RunRequest,
                adapter.RootPath,
                review,
                iteration,
                this._orchestrationAgent.Id,
                this._orchestrationAgent.Role,
                cancellationToken);

            string latestDiff = await adapter.DiffAsync(cancellationToken);
            progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, "Style", "Style enforcement prompt started", remediationPrompt));
            await this._styleAgent.EnforceAsync(
                new StyleEnforcementRequest(
                    DelegatedPrompt: remediationPrompt,
                    Diff: latestDiff,
                    WorkspaceRoot: adapter.RootPath,
                    FilesTouched: currentFiles,
                    LanguageScope: request.ArchitectureLanguages,
                    ModelOverrides: request.RunRequest.ModelOverrides),
                this._styleAgent.Id,
                this._styleAgent.Role,
                cancellationToken);

            progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, "Architecture", "Enforcement prompt started", remediationPrompt));

            latestDiff = await adapter.DiffAsync(cancellationToken);
            string delegatedPrompt = request.RunRequest.ArchitectureLoopMode
                ? ArchitectureLoopHelpers.BuildArchitectureLoopPrompt(remediationPrompt, adapter.RootPath, request.RunRequest.ArchitectureLoopPrompt)
                : remediationPrompt;
            review = await this._architectureAgent.ReviewAsync(
                new ArchitectureReviewRequest(
                    DelegatedPrompt: delegatedPrompt,
                    Diff: latestDiff,
                    WorkspaceRoot: adapter.RootPath,
                    FilesTouched: currentFiles,
                    LanguageScope: request.ArchitectureLanguages,
                    ModelOverrides: request.RunRequest.ModelOverrides),
                this._architectureAgent.Id,
                this._architectureAgent.Role,
                cancellationToken);

            string currentFindingsFingerprint = BuildFindingsFingerprint(review.Findings);
            if (string.Equals(previousFindingsFingerprint, currentFindingsFingerprint, StringComparison.Ordinal))
            {
                string[] blockedActions = review.RequiredActions
                    .Concat(new[] { NO_PROGRESS_BLOCKED_STATUS })
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                review = review with { RequiredActions = blockedActions };
                progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, "architecture-loop", "Review blocked due to identical findings across iterations."));
                break;
            }

            previousFindingsFingerprint = currentFindingsFingerprint;
            currentFiles = request.RunRequest.ArchitectureLoopMode
                ? ArchitectureLoopHelpers.EnumerateWorkspaceFiles(adapter.RootPath, request.ArchitectureLanguages)
                : currentFiles
                    .Concat(ParseTouchedFiles(latestDiff))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
        }

        return (review, currentFiles);
    }

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
}
