using ArchHarness.App.Constants;

namespace ArchHarness.App.Core;

/// <summary>
/// Converts review and verification evidence into structured follow-up hints that the orchestrator
/// can promote into append-only remediation waves.
/// </summary>
public static class ReplanningSignalBuilder
{
    private const int MAX_REVIEW_HINTS = 8;
    private const int MAX_VERIFICATION_HINTS = 8;

    /// <summary>
    /// Builds follow-up hints from unresolved architecture and security review results.
    /// </summary>
    public static IReadOnlyList<StepFollowUpHint> BuildReviewHints(
        ArchitectureReview review,
        SecurityReview securityReview,
        IReadOnlyList<string> filesTouched,
        IReadOnlyList<string>? architectureLanguages = null,
        IReadOnlyList<string>? securityLanguages = null)
    {
        List<StepFollowUpHint> hints = new();

        foreach (ArchitectureFinding finding in review.Findings.Where(IsHighImpact).Take(MAX_REVIEW_HINTS))
        {
            hints.Add(new StepFollowUpHint(
                ResolveImplementationAgent(finding.File, filesTouched),
                BuildArchitectureObjective(finding),
                $"architecture-review:{finding.Severity}:{finding.Rule}",
                architectureLanguages));
        }

        foreach (SecurityFinding finding in securityReview.Findings.Where(IsHighImpact).Take(MAX_REVIEW_HINTS - hints.Count))
        {
            hints.Add(new StepFollowUpHint(
                ResolveImplementationAgent(finding.File, filesTouched),
                BuildSecurityObjective(finding),
                $"security-review:{finding.Severity}:{finding.Rule}",
                securityLanguages));
        }

        foreach (string action in review.RequiredActions.Concat(securityReview.RequiredActions)
            .Where(action => !string.IsNullOrWhiteSpace(action))
            .Where(action => !string.Equals(action, ArchitectureReviewLoop.NO_PROGRESS_BLOCKED_STATUS, StringComparison.OrdinalIgnoreCase))
            .Take(MAX_REVIEW_HINTS - hints.Count))
        {
            hints.Add(new StepFollowUpHint(
                ResolveImplementationAgent(null, filesTouched),
                $"Resolve review required action: {action.Trim()}",
                "review-required-action"));
        }

        return Deduplicate(hints).Take(MAX_REVIEW_HINTS).ToArray();
    }

    /// <summary>
    /// Builds follow-up hints from failed completion validation results.
    /// </summary>
    public static IReadOnlyList<StepFollowUpHint> BuildVerificationHints(
        CompletionValidationResult validationResult,
        ClarificationSpec? spec,
        ExecutionPlan plan,
        BuildOutcome? lastBuildOutcome,
        IReadOnlyList<string> filesTouched)
    {
        if (validationResult.Passed)
        {
            return Array.Empty<StepFollowUpHint>();
        }

        List<StepFollowUpHint> hints = new();
        string desiredOutcome = spec?.DesiredOutcome ?? string.Join("; ", plan.CompletionCriteria);
        foreach (CriterionResult criterion in validationResult.CriterionResults.Where(result => !result.Passed).Take(MAX_VERIFICATION_HINTS))
        {
            hints.Add(new StepFollowUpHint(
                ResolveImplementationAgent(null, filesTouched),
                BuildCriterionObjective(criterion, desiredOutcome),
                "verification-failed-criterion"));
        }

        if (validationResult.Assessment is { } assessment)
        {
            foreach (string gap in assessment.Gaps.Where(gap => !string.IsNullOrWhiteSpace(gap)).Take(MAX_VERIFICATION_HINTS - hints.Count))
            {
                hints.Add(new StepFollowUpHint(
                    ResolveImplementationAgent(null, filesTouched),
                    $"Close verifier-identified implementation gap: {gap.Trim()}",
                    "verification-assessment-gap"));
            }
        }

        foreach (VerificationEvidence evidence in validationResult.Evidence?.Where(evidence => !evidence.Passed) ?? Array.Empty<VerificationEvidence>())
        {
            if (hints.Count >= MAX_VERIFICATION_HINTS)
            {
                break;
            }

            hints.Add(new StepFollowUpHint(
                ResolveImplementationAgentFromCommand(evidence.Command, filesTouched),
                BuildEvidenceObjective(evidence),
                $"verification-evidence:{evidence.Type}:{evidence.Name}"));
        }

        if (lastBuildOutcome is { Passed: false } buildOutcome && hints.Count < MAX_VERIFICATION_HINTS)
        {
            hints.Add(new StepFollowUpHint(
                ResolveImplementationAgentFromCommand(buildOutcome.Command ?? string.Empty, filesTouched),
                $"Fix the failing build command '{buildOutcome.Command ?? "build"}'. Evidence: {TrimForObjective(buildOutcome.Summary)}",
                "verification-build-failure"));
        }

        return Deduplicate(hints).Take(MAX_VERIFICATION_HINTS).ToArray();
    }

    private static string BuildArchitectureObjective(ArchitectureFinding finding)
    {
        string location = BuildLocation(finding.File, finding.Symbol);
        return $"Remediate high-impact architecture finding '{finding.Rule}'{location}: {TrimForObjective(finding.Rationale)}";
    }

    private static string BuildSecurityObjective(SecurityFinding finding)
    {
        string location = BuildLocation(finding.File, finding.Symbol);
        return $"Remediate high-impact security finding '{finding.Rule}'{location}: {TrimForObjective(finding.Rationale)}";
    }

    private static string BuildCriterionObjective(CriterionResult criterion, string desiredOutcome)
        => $"Address unmet completion criterion '{TrimForObjective(criterion.Criterion)}' for the original objective '{TrimForObjective(desiredOutcome)}'. Evidence: {TrimForObjective(criterion.Evidence)}";

    private static string BuildEvidenceObjective(VerificationEvidence evidence)
        => $"Fix failing verification '{evidence.Name}' ({evidence.Command}). Evidence: {TrimForObjective(evidence.Summary)}";

    private static string BuildLocation(string? file, string? symbol)
    {
        if (string.IsNullOrWhiteSpace(file) && string.IsNullOrWhiteSpace(symbol))
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(symbol))
        {
            return $" in {file}";
        }

        if (string.IsNullOrWhiteSpace(file))
        {
            return $" around {symbol}";
        }

        return $" in {file} around {symbol}";
    }

    private static bool IsHighImpact(ArchitectureFinding finding)
        => IsHighImpactSeverity(finding.Severity);

    private static bool IsHighImpact(SecurityFinding finding)
        => IsHighImpactSeverity(finding.Severity);

    private static bool IsHighImpactSeverity(string? severity)
        => string.Equals(severity, Severities.HIGH, StringComparison.OrdinalIgnoreCase)
            || string.Equals(severity, "critical", StringComparison.OrdinalIgnoreCase);

    private static string ResolveImplementationAgent(string? file, IReadOnlyList<string> filesTouched)
    {
        string? target = !string.IsNullOrWhiteSpace(file)
            ? file
            : filesTouched.FirstOrDefault();
        return LooksFrontend(target) ? AgentNames.FRONTEND_DEVELOPER : AgentNames.BACKEND_DEVELOPER;
    }

    private static string ResolveImplementationAgentFromCommand(string command, IReadOnlyList<string> filesTouched)
    {
        if (command.Contains("npm", StringComparison.OrdinalIgnoreCase)
            || command.Contains("vite", StringComparison.OrdinalIgnoreCase)
            || command.Contains("eslint", StringComparison.OrdinalIgnoreCase))
        {
            return AgentNames.FRONTEND_DEVELOPER;
        }

        return ResolveImplementationAgent(null, filesTouched);
    }

    private static bool LooksFrontend(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string extension = Path.GetExtension(path);
        return extension.Equals(".js", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jsx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".ts", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".tsx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".vue", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".css", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".scss", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".html", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<StepFollowUpHint> Deduplicate(IEnumerable<StepFollowUpHint> hints)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (StepFollowUpHint hint in hints)
        {
            string signature = $"{hint.Agent}::{hint.Objective.Trim()}";
            if (seen.Add(signature))
            {
                yield return hint;
            }
        }
    }

    private static string TrimForObjective(string? value)
    {
        string text = (value ?? string.Empty).ReplaceLineEndings(" ").Trim();
        return text.Length <= 220 ? text : text[..217] + "...";
    }
}
