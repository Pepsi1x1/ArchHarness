using ArchHarness.App.Agents;
using ArchHarness.App.Workspace;

namespace ArchHarness.App.Core;

/// <summary>
/// Owns the architecture review remediation iteration loop, invoking the Architecture agent
/// to re-review after remediation and enforcing the maximum iteration limit.
/// </summary>
public sealed class ArchitectureReviewLoop
{
    private readonly OrchestrationAgent _orchestrationAgent;
    private readonly ArchitectureAgent _architectureAgent;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArchitectureReviewLoop"/> class.
    /// </summary>
    /// <param name="orchestrationAgent">Agent used to build remediation prompts.</param>
    /// <param name="architectureAgent">Agent used to perform architecture reviews.</param>
    public ArchitectureReviewLoop(
        OrchestrationAgent orchestrationAgent,
        ArchitectureAgent architectureAgent)
    {
        _orchestrationAgent = orchestrationAgent;
        _architectureAgent = architectureAgent;
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
        var review = request.InitialReview;
        var currentFiles = request.FilesTouched;
        var iteration = 0;

        while (request.IterationStrategy.ReviewRequired &&
               review.Findings.Any(f => f.Severity.Equals("high", StringComparison.OrdinalIgnoreCase)) &&
               iteration < request.IterationStrategy.MaxIterations)
        {
            iteration++;
            progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, "architecture-loop", $"Review iteration {iteration}"));

            var remediationPrompt = await _orchestrationAgent.BuildRemediationPromptAsync(
                request.RunRequest,
                adapter.RootPath,
                review,
                iteration,
                _orchestrationAgent.Id,
                _orchestrationAgent.Role,
                cancellationToken);

            progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, "Architecture", "Enforcement prompt started", remediationPrompt));

            var latestDiff = await adapter.DiffAsync(cancellationToken);
            review = await _architectureAgent.ReviewAsync(
                new ArchitectureReviewRequest(
                    DelegatedPrompt: remediationPrompt,
                    Diff: latestDiff,
                    WorkspaceRoot: adapter.RootPath,
                    FilesTouched: currentFiles,
                    LanguageScope: request.ArchitectureLanguages,
                    ModelOverrides: request.RunRequest.ModelOverrides),
                _architectureAgent.Id,
                _architectureAgent.Role,
                cancellationToken);

            currentFiles = ParseTouchedFiles(latestDiff);
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
}
