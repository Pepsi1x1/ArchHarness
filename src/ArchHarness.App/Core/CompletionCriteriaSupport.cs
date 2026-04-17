using ArchHarness.App.Constants;

namespace ArchHarness.App.Core;

/// <summary>
/// Shared helpers for recognizing and evaluating supported completion criteria.
/// </summary>
internal static class CompletionCriteriaSupport
{
    public static bool IsSupportedPlanCriterion(string criterion)
        => IsBuildCriterion(criterion)
            || IsSecurityCriterion(criterion)
            || IsArchitectureCriterion(criterion)
            || IsCodingStyleCriterion(criterion)
            || WikiDocCompletionCriteriaSupport.IsSupportedCriterion(criterion);

    public static bool IsBuildCriterion(string criterion)
        => Normalize(criterion).Contains("build", StringComparison.Ordinal);

    public static bool IsSecurityCriterion(string criterion)
        => Normalize(criterion).Contains("security", StringComparison.Ordinal);

    public static bool IsArchitectureCriterion(string criterion)
        => Normalize(criterion).Contains("architecture", StringComparison.Ordinal);

    public static bool IsCodingStyleCriterion(string criterion)
    {
        string normalized = Normalize(criterion);
        return normalized.Contains("coding style", StringComparison.Ordinal)
            || normalized.Contains("style", StringComparison.Ordinal)
            || normalized.Contains("naming convention", StringComparison.Ordinal);
    }

    public static CriterionResult EvaluateCriterion(
        string criterion,
        CompletionValidationRequest request,
        ReviewLoopAgentSelection reviewLoopAgents)
    {
        if (IsBuildCriterion(criterion))
        {
            return EvaluateBuildCriterion(criterion, request);
        }

        if (IsArchitectureCriterion(criterion))
        {
            return EvaluateArchitectureCriterion(criterion, request, reviewLoopAgents);
        }

        if (IsSecurityCriterion(criterion))
        {
            return EvaluateSecurityCriterion(criterion, request, reviewLoopAgents);
        }

        if (IsCodingStyleCriterion(criterion))
        {
            return EvaluateCodingStyleCriterion(criterion, request);
        }

        if (WikiDocCompletionCriteriaSupport.IsSupportedCriterion(criterion))
        {
            return WikiDocCompletionCriteriaSupport.EvaluateCriterion(criterion, request);
        }

        VerificationEvidence? explicitEvidence = FindEvidenceForCriterion(request.VerificationEvidence, criterion);
        if (explicitEvidence is not null)
        {
            return new CriterionResult(criterion, explicitEvidence.Passed, explicitEvidence.Summary);
        }

        return new CriterionResult(
            criterion,
            false,
            $"Criterion '{criterion}' does not have executable verification evidence and is not a supported built-in criterion.");
    }

    private static CriterionResult EvaluateBuildCriterion(string criterion, CompletionValidationRequest request)
    {
        VerificationEvidence? buildEvidence = FindLatestBuildEvidence(request.VerificationEvidence, criterion);
        if (buildEvidence is not null)
        {
            bool requireSuccess = !Normalize(criterion).Contains("summarized", StringComparison.Ordinal);
            return new CriterionResult(criterion, !requireSuccess || buildEvidence.Passed, buildEvidence.Summary);
        }

        if (request.BuildOutcome is null)
        {
            return new CriterionResult(criterion, false, "No build outcome or build verification evidence was recorded during execution.");
        }

        bool allowSummaryOnly = Normalize(criterion).Contains("summarized", StringComparison.Ordinal);
        return new CriterionResult(criterion, allowSummaryOnly || request.BuildOutcome.Passed, request.BuildOutcome.Summary);
    }

    private static CriterionResult EvaluateArchitectureCriterion(
        string criterion,
        CompletionValidationRequest request,
        ReviewLoopAgentSelection reviewLoopAgents)
    {
        if (!reviewLoopAgents.ArchitectureEnabled
            || string.Equals(request.Workflow, WorkflowNames.WIKIDOC, StringComparison.OrdinalIgnoreCase))
        {
            return new CriterionResult(criterion, true, "Architecture review is disabled for this run.");
        }

        int highCount = request.Review.Findings.Count(f => string.Equals(f.Severity, "high", StringComparison.OrdinalIgnoreCase));
        return new CriterionResult(
            criterion,
            highCount == 0,
            highCount == 0 ? "No high-severity architecture findings." : $"{highCount} high-severity architecture finding(s) remain.");
    }

    private static CriterionResult EvaluateSecurityCriterion(
        string criterion,
        CompletionValidationRequest request,
        ReviewLoopAgentSelection reviewLoopAgents)
    {
        if (!reviewLoopAgents.SecurityEnabled)
        {
            return new CriterionResult(criterion, true, "Security review is disabled for this run.");
        }

        int highCount = request.SecurityReview.Findings.Count(f => string.Equals(f.Severity, "high", StringComparison.OrdinalIgnoreCase));
        return new CriterionResult(
            criterion,
            highCount == 0,
            highCount == 0 ? "No high-severity security findings." : $"{highCount} high-severity security finding(s) remain.");
    }

    private static CriterionResult EvaluateCodingStyleCriterion(string criterion, CompletionValidationRequest request)
    {
        VerificationEvidence? styleEvidence = FindEvidenceForCriterion(request.VerificationEvidence, criterion);
        if (styleEvidence is not null)
        {
            return new CriterionResult(criterion, styleEvidence.Passed, styleEvidence.Summary);
        }

        bool codingStyleStepExecuted = request.Plan.Steps.Any(step => string.Equals(step.Agent, "CodingStyle", StringComparison.OrdinalIgnoreCase));
        return new CriterionResult(
            criterion,
            codingStyleStepExecuted,
            codingStyleStepExecuted
                ? "Coding style enforcement step executed during the run."
                : "No coding style evidence or execution step was recorded.");
    }

    public static VerificationEvidence? FindEvidenceForCriterion(IReadOnlyList<VerificationEvidence>? evidence, string criterion)
    {
        if (evidence is not { Count: > 0 })
        {
            return null;
        }

        return evidence.LastOrDefault(item => MatchesCriterion(item, criterion));
    }

    public static VerificationEvidence? FindLatestBuildEvidence(IReadOnlyList<VerificationEvidence>? evidence, string? criterion = null)
    {
        if (evidence is not { Count: > 0 })
        {
            return null;
        }

        return evidence.LastOrDefault(item => string.Equals(item.Type, "build", StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(criterion) && MatchesCriterion(item, criterion)));
    }

    private static bool MatchesCriterion(VerificationEvidence evidence, string criterion)
    {
        string normalizedCriterion = Normalize(criterion);
        if (!string.IsNullOrWhiteSpace(evidence.Criterion)
            && string.Equals(evidence.Criterion.Trim(), criterion.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(evidence.Name)
            && Normalize(evidence.Name) == normalizedCriterion)
        {
            return true;
        }

        return false;
    }

    private static string Normalize(string value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant();
}
