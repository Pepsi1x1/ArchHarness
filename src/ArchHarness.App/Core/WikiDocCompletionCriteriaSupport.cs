using System.Text.Json;
using System.Text.RegularExpressions;
using ArchHarness.App.Constants;

namespace ArchHarness.App.Core;

/// <summary>
/// Deterministic completion checks for wikidoc workflow criteria.
/// </summary>
internal static partial class WikiDocCompletionCriteriaSupport
{
    public static bool IsSupportedCriterion(string criterion)
    {
        string normalized = Normalize(criterion);
        return normalized.Contains("wikidoc", StringComparison.Ordinal)
            || normalized.Contains("megawiki", StringComparison.Ordinal)
            || normalized.Contains("wiki-documentation workflow", StringComparison.Ordinal)
            || normalized.Contains("per-repository wiki", StringComparison.Ordinal)
            || normalized.Contains("cross-repository concept", StringComparison.Ordinal)
            || normalized.Contains("scan root", StringComparison.Ordinal) && normalized.Contains("repository", StringComparison.Ordinal)
            || normalized.Contains("isolated documentation session", StringComparison.Ordinal)
            || normalized.Contains("documentation folder", StringComparison.Ordinal) && normalized.Contains("safe to rename", StringComparison.Ordinal)
            || normalized.Contains("repo-local `wiki` output", StringComparison.Ordinal)
            || normalized.Contains("alternate output location", StringComparison.Ordinal)
            || normalized.Contains("active-run stream", StringComparison.Ordinal)
            || normalized.Contains("generated wiki artifacts", StringComparison.Ordinal)
            || normalized.Contains("operator-facing documentation", StringComparison.Ordinal);
    }

    public static CriterionResult EvaluateCriterion(string criterion, CompletionValidationRequest request)
    {
        WikiDocVerificationContext? context = TryBuildContext(request);
        if (context is null)
        {
            return new CriterionResult(criterion, false, "WikiDoc verification report was not available for deterministic evaluation.");
        }

        string normalized = Normalize(criterion);
        if (normalized.Contains("repository discovery processes each unique git repository", StringComparison.Ordinal))
        {
            return EvaluateRepositoryDiscovery(criterion, context);
        }

        if (normalized.Contains("one isolated documentation session for each discovered repository", StringComparison.Ordinal))
        {
            return EvaluateSessionIsolation(criterion, context);
        }

        if (normalized.Contains("safe to rename", StringComparison.Ordinal) && normalized.Contains("documentation folder", StringComparison.Ordinal))
        {
            return EvaluateRenameBehavior(criterion, context);
        }

        if (normalized.Contains("repo-local `wiki` output is available", StringComparison.Ordinal)
            || normalized.Contains("writes markdown under `wiki\\` with `home.md`", StringComparison.Ordinal))
        {
            return EvaluateRepositoryHomeOutputs(criterion, context);
        }

        if (normalized.Contains("repo-local `wiki` cannot be created", StringComparison.Ordinal)
            || normalized.Contains("deterministic explicit alternate output location", StringComparison.Ordinal))
        {
            return EvaluateFallbackBehavior(criterion, context);
        }

        if (normalized.Contains("megawiki\\wiki\\home.md", StringComparison.Ordinal))
        {
            return EvaluateMegaWikiOutput(criterion, context);
        }

        if (normalized.Contains("cross-repository concept markdown page", StringComparison.Ordinal))
        {
            return EvaluateConceptPages(criterion, context);
        }

        if (normalized.Contains("existing web/electron run experience", StringComparison.Ordinal))
        {
            return EvaluateWebAndStreamWiring(criterion, context);
        }

        if (normalized.Contains("writes only generated wiki artifacts", StringComparison.Ordinal))
        {
            return EvaluateOutputScope(criterion, context);
        }

        if (normalized.Contains("operator-facing documentation describes", StringComparison.Ordinal))
        {
            return EvaluateOperatorDocs(criterion, context);
        }

        return new CriterionResult(
            criterion,
            false,
            $"Criterion '{criterion}' is recognized as wikidoc-specific, but no deterministic evaluator matched it.");
    }

    private static WikiDocVerificationContext? TryBuildContext(CompletionValidationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RunDirectory))
        {
            return null;
        }

        string reportPath = Path.Combine(request.RunDirectory, "WikiDocReport.json");
        if (!File.Exists(reportPath))
        {
            return null;
        }

        WikiDocExecutionReport? report = JsonSerializer.Deserialize<WikiDocExecutionReport>(File.ReadAllText(reportPath));
        if (report is null)
        {
            return null;
        }

        string scanRoot = string.IsNullOrWhiteSpace(request.WorkspaceRoot)
            ? report.ScanRoot
            : Path.GetFullPath(request.WorkspaceRoot);
        string runDirectory = Path.GetFullPath(request.RunDirectory);
        string? harnessRoot = FindHarnessRoot();
        IReadOnlyList<string> discoveredRepositories = new WikiDocRepositoryDiscoverer()
            .Discover(scanRoot)
            .Select(repository => Path.GetFullPath(repository.RepositoryRoot))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new WikiDocVerificationContext(scanRoot, runDirectory, report, discoveredRepositories, harnessRoot);
    }

    private static CriterionResult EvaluateRepositoryDiscovery(string criterion, WikiDocVerificationContext context)
    {
        string[] reportedRepositories = context.Report.RepositoryOutputs
            .Select(output => Path.GetFullPath(output.RepositoryRoot))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        bool passed = reportedRepositories.SequenceEqual(
            context.DiscoveredRepositories.OrderBy(path => path, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        return new CriterionResult(
            criterion,
            passed,
            passed
                ? $"Repository discovery matched {reportedRepositories.Length} unique Git repositories including the scan root when applicable."
                : "Repository outputs did not exactly match the Git repositories discovered under the scan root.");
    }

    private static CriterionResult EvaluateSessionIsolation(string criterion, WikiDocVerificationContext context)
    {
        string[] sessionKeys = context.Report.RepositoryOutputs
            .Select(output => output.DocumentationSessionKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        bool passed = context.Report.RepositoryOutputs.Count == context.DiscoveredRepositories.Count
            && sessionKeys.Length == context.DiscoveredRepositories.Count
            && context.Report.RepositoryOutputs
                .Select(output => Path.GetFullPath(output.RepositoryRoot))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() == context.DiscoveredRepositories.Count;
        return new CriterionResult(
            criterion,
            passed,
            passed
                ? $"Tracked one documentation session key per discovered repository ({sessionKeys.Length} total)."
                : "The report did not record a unique documentation session key for every discovered repository.");
    }

    private static CriterionResult EvaluateRenameBehavior(string criterion, WikiDocVerificationContext context)
    {
        WikiDocRepositoryOutput[] renameEligible = context.Report.RepositoryOutputs
            .Where(output => output.RenameCandidateWasEligible)
            .ToArray();
        bool passed = renameEligible.All(output =>
            !output.UsedFallback
            && string.Equals(output.OutputRoot, output.RequestedLocalRoot, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetFileName(output.OutputRoot), "wiki", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(output.RenamedFrom));
        string evidence = renameEligible.Length == 0
            ? "No safe documentation folders required rename handling."
            : passed
                ? $"Safely adopted {renameEligible.Length} documentation folder(s) as repository-local wiki output."
                : "At least one safe documentation folder was not renamed or adopted as the repository-local wiki.";
        return new CriterionResult(
            criterion,
            passed,
            evidence);
    }

    private static CriterionResult EvaluateRepositoryHomeOutputs(string criterion, WikiDocVerificationContext context)
    {
        WikiDocRepositoryOutput[] repoLocalOutputs = context.Report.RepositoryOutputs
            .Where(output => !output.UsedFallback)
            .ToArray();
        bool passed = repoLocalOutputs.All(output =>
            string.Equals(output.OutputRoot, Path.Combine(output.RepositoryRoot, "wiki"), StringComparison.OrdinalIgnoreCase)
            && string.Equals(output.HomePath, Path.Combine(output.OutputRoot, "Home.md"), StringComparison.OrdinalIgnoreCase)
            && File.Exists(output.HomePath)
            && HasOnlyRelativeLinks(output.HomePath));
        return new CriterionResult(
            criterion,
            passed,
            passed
                ? $"Verified {repoLocalOutputs.Length} repository-local wiki Home.md output(s) with publishable relative links."
                : "One or more repository-local wiki outputs were missing Home.md or contained non-relative links.");
    }

    private static CriterionResult EvaluateFallbackBehavior(string criterion, WikiDocVerificationContext context)
    {
        WikiDocRepositoryOutput[] fallbackOutputs = context.Report.RepositoryOutputs
            .Where(output => output.UsedFallback)
            .ToArray();
        bool passed = fallbackOutputs.All(output =>
            output.OutputRoot.StartsWith(Path.Combine(context.RunDirectory, "wikidoc-fallback"), StringComparison.OrdinalIgnoreCase)
            && context.Report.Fallbacks.Any(fallback =>
                string.Equals(fallback.Scope, "repository", StringComparison.OrdinalIgnoreCase)
                && string.Equals(fallback.OwnerRoot, output.RepositoryRoot, StringComparison.OrdinalIgnoreCase)
                && string.Equals(fallback.RequestedLocalRoot, output.RequestedLocalRoot, StringComparison.OrdinalIgnoreCase)
                && string.Equals(fallback.FallbackRoot, output.OutputRoot, StringComparison.OrdinalIgnoreCase)));
        string evidence = fallbackOutputs.Length == 0
            ? "No repository required fallback wiki output."
            : passed
                ? $"Verified {fallbackOutputs.Length} deterministic repository fallback output location(s)."
                : "Fallback repository output was missing or not recorded deterministically.";
        return new CriterionResult(
            criterion,
            passed,
            evidence);
    }

    private static CriterionResult EvaluateMegaWikiOutput(string criterion, WikiDocVerificationContext context)
    {
        string expectedHomePath = Path.Combine(context.ScanRoot, "megawiki", "wiki", "Home.md");
        string actualHomePath = Path.GetFullPath(context.Report.AggregateOutput.MegaWikiPath);
        bool linksAllRepositories = File.Exists(actualHomePath)
            && context.Report.RepositoryOutputs.All(output =>
                File.ReadAllText(actualHomePath).Contains($"({ToMarkdownRelativePath(context.Report.AggregateOutput.OutputRoot, output.HomePath)})", StringComparison.Ordinal));
        bool passed = !context.Report.AggregateOutput.UsedFallback
            && string.Equals(actualHomePath, expectedHomePath, StringComparison.OrdinalIgnoreCase)
            && linksAllRepositories;
        return new CriterionResult(
            criterion,
            passed,
            passed
                ? $"Megawiki home was generated at {expectedHomePath} and linked every repository wiki Home.md."
                : "Megawiki home was missing, used fallback output, or did not link every repository wiki Home.md.");
    }

    private static CriterionResult EvaluateConceptPages(string criterion, WikiDocVerificationContext context)
    {
        if (context.Report.RepositoryOutputs.Count < 2)
        {
            return new CriterionResult(criterion, true, "Only one repository was discovered, so no cross-repository concept synthesis was required.");
        }

        string megaWikiHome = context.Report.AggregateOutput.MegaWikiPath;
        bool passed = context.Report.AggregateOutput.ConceptPagePaths.Count > 0
            && File.Exists(megaWikiHome)
            && context.Report.AggregateOutput.ConceptPagePaths.All(File.Exists)
            && context.Report.AggregateOutput.ConceptPagePaths.Any(path =>
                File.ReadAllText(megaWikiHome).Contains($"({ToMarkdownRelativePath(context.Report.AggregateOutput.OutputRoot, path)})", StringComparison.Ordinal));
        return new CriterionResult(
            criterion,
            passed,
            passed
                ? $"Megawiki linked {context.Report.AggregateOutput.ConceptPagePaths.Count} generated cross-repository concept page(s)."
                : "Megawiki did not include a linked generated cross-repository concept page.");
    }

    private static CriterionResult EvaluateWebAndStreamWiring(string criterion, WikiDocVerificationContext context)
    {
        if (string.IsNullOrWhiteSpace(context.HarnessRoot))
        {
            return new CriterionResult(criterion, false, "Could not locate the ArchHarness source root for web workflow verification.");
        }

        string handlersPath = Path.Combine(context.HarnessRoot, "src", "ArchHarness.Web", "Program.Handlers.cs");
        string endpointsPath = Path.Combine(context.HarnessRoot, "src", "ArchHarness.Web", "ProgramEndpointExtensions.cs");
        string sessionManagerPath = Path.Combine(context.HarnessRoot, "src", "ArchHarness.Web", "Services", "WebRunSessionManager.cs");
        bool passed = File.Exists(handlersPath)
            && File.ReadAllText(handlersPath).Contains("WorkflowNames.WIKIDOC", StringComparison.Ordinal)
            && File.ReadAllText(handlersPath).Contains("RunRequestWorkflowDefaults.Apply", StringComparison.Ordinal)
            && File.Exists(endpointsPath)
            && File.ReadAllText(endpointsPath).Contains("/api/runs/active/events", StringComparison.Ordinal)
            && File.Exists(sessionManagerPath)
            && File.ReadAllText(sessionManagerPath).Contains("ReadEventsAsync", StringComparison.Ordinal)
            && File.ReadAllText(sessionManagerPath).Contains("StartRunAsync", StringComparison.Ordinal);
        return new CriterionResult(
            criterion,
            passed,
            passed
                ? "Web run start and active-run event stream wiring includes wikidoc workflow support."
                : "Web run start or active-run event stream wiring for wikidoc could not be verified from source.");
    }

    private static CriterionResult EvaluateOutputScope(string criterion, WikiDocVerificationContext context)
    {
        string[] generatedFiles = context.Report.RepositoryOutputs
            .Select(output => output.HomePath)
            .Concat(context.Report.AggregateOutput.ConceptPagePaths)
            .Append(context.Report.AggregateOutput.MegaWikiPath)
            .Select(Path.GetFullPath)
            .ToArray();
        bool passed = generatedFiles.All(path =>
            context.Report.RepositoryOutputs.Any(output => path.StartsWith(Path.GetFullPath(output.OutputRoot), StringComparison.OrdinalIgnoreCase))
            || path.StartsWith(Path.GetFullPath(context.Report.AggregateOutput.OutputRoot), StringComparison.OrdinalIgnoreCase));
        return new CriterionResult(
            criterion,
            passed,
            passed
                ? $"Verified {generatedFiles.Length} generated wiki artifact path(s) stayed inside the selected wiki output roots."
                : "A generated wiki artifact was recorded outside its selected repository or megawiki output root.");
    }

    private static CriterionResult EvaluateOperatorDocs(string criterion, WikiDocVerificationContext context)
    {
        if (string.IsNullOrWhiteSpace(context.HarnessRoot))
        {
            return new CriterionResult(criterion, false, "Could not locate README.md for wikidoc operator documentation verification.");
        }

        string readmePath = Path.Combine(context.HarnessRoot, "README.md");
        bool passed = File.Exists(readmePath)
            && File.ReadAllText(readmePath).Contains("wikidoc", StringComparison.OrdinalIgnoreCase)
            && File.ReadAllText(readmePath).Contains("wiki\\Home.md", StringComparison.OrdinalIgnoreCase)
            && File.ReadAllText(readmePath).Contains("megawiki\\wiki\\Home.md", StringComparison.OrdinalIgnoreCase)
            && File.ReadAllText(readmePath).Contains("wikidoc-fallback", StringComparison.OrdinalIgnoreCase);
        return new CriterionResult(
            criterion,
            passed,
            passed
                ? "README documents the wikidoc command, per-repository wiki output, megawiki output, and fallback behavior."
                : "README is missing required wikidoc operator guidance.");
    }

    private static bool HasOnlyRelativeLinks(string markdownPath)
    {
        string content = File.ReadAllText(markdownPath);
        foreach (Match match in MarkdownLinkRegex().Matches(content))
        {
            string target = match.Groups[1].Value.Trim();
            if (string.IsNullOrWhiteSpace(target) || target.StartsWith('#'))
            {
                continue;
            }

            if (Path.IsPathRooted(target) || Uri.TryCreate(target, UriKind.Absolute, out _))
            {
                return false;
            }
        }

        return true;
    }

    private static string ToMarkdownRelativePath(string baseDirectory, string targetPath)
        => Path.GetRelativePath(baseDirectory, targetPath).Replace('\\', '/');

    private static string? FindHarnessRoot()
    {
        DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = current.FullName;
            if (File.Exists(Path.Combine(candidate, "README.md"))
                && File.Exists(Path.Combine(candidate, "src", "ArchHarness.Web", "Program.Handlers.cs")))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }

    private static string Normalize(string value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant();

    [GeneratedRegex(@"\[[^\]]+\]\(([^)]+)\)")]
    private static partial Regex MarkdownLinkRegex();

    private sealed record WikiDocVerificationContext(
        string ScanRoot,
        string RunDirectory,
        WikiDocExecutionReport Report,
        IReadOnlyList<string> DiscoveredRepositories,
        string? HarnessRoot);
}
