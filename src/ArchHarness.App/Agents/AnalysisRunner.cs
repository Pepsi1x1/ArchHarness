using ArchHarness.App.Agents.Analyzers;
using ArchHarness.App.Constants;
using ArchHarness.App.Core;
using Microsoft.CodeAnalysis.CSharp;

namespace ArchHarness.App.Agents;

/// <summary>
/// Invokes static architecture analyzers on parsed source files and aggregates findings.
/// </summary>
internal static class AnalysisRunner
{
    private const string SEVERITY_HIGH = Severities.HIGH;
    private const string SEVERITY_MEDIUM = Severities.MEDIUM;

    private static readonly IArchitectureAnalyzer SRP_ANALYZER = new SrpAnalyzer();
    private static readonly IArchitectureAnalyzer DIP_ANALYZER = new DipAnalyzer();
    private static readonly IArchitectureAnalyzer ISP_ANALYZER = new IspAnalyzer();
    private static readonly IArchitectureAnalyzer OCP_LSP_ANALYZER = new OcpLspAnalyzer();
    private static readonly IArchitectureAnalyzer DRY_ANALYZER = new DryAnalyzer();

    /// <summary>
    /// Runs all static analyzers against the diff and workspace files, returning an architecture review.
    /// </summary>
    /// <param name="diff">The current diff snapshot.</param>
    /// <param name="workspaceRoot">The workspace root path.</param>
    /// <param name="filesTouched">Files modified during the run.</param>
    /// <returns>The aggregated architecture review.</returns>
    public static ArchitectureReview Analyze(
        string diff,
        string workspaceRoot,
        IReadOnlyList<string> filesTouched,
        ArchitectureAnalyzerOptions? analyzerOptions = null)
    {
        ArchitectureAnalyzerOptions options = analyzerOptions ?? new ArchitectureAnalyzerOptions();
        List<ArchitectureFinding> findings = new List<ArchitectureFinding>();
        HashSet<string> requiredActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (options.CompletenessTodo && diff.Contains("TODO", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new ArchitectureFinding(SEVERITY_HIGH, "Completeness", filesTouched.FirstOrDefault(), "TODO", "TODO marker found in implementation."));
            requiredActions.Add("Remove TODO markers and complete implementation details.");
        }

        List<string> candidateFiles = ResolveCandidateFiles(diff, workspaceRoot);
        if (candidateFiles.Count == 0)
        {
            return new ArchitectureReview(findings, requiredActions.ToArray());
        }

        List<ParsedFile> parsedFiles = ParseFiles(candidateFiles);
        foreach (IArchitectureAnalyzer analyzer in GetEnabledAnalyzers(options))
        {
            analyzer.Analyze(parsedFiles, findings, requiredActions);
        }

        bool hasTests = filesTouched.Any(f => f.Contains("test", StringComparison.OrdinalIgnoreCase))
            || candidateFiles.Any(f => f.Contains("test", StringComparison.OrdinalIgnoreCase));
        if (options.MissingTests && !hasTests && candidateFiles.Any(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            findings.Add(new ArchitectureFinding(
                SEVERITY_MEDIUM,
                "SeparationOfConcerns",
                Path.GetRelativePath(workspaceRoot, candidateFiles[0]),
                "Tests",
                "Code changes were detected without corresponding tests."
            ));
            requiredActions.Add("Add or update tests that cover the implemented behavior.");
        }

        return new ArchitectureReview(findings, requiredActions.ToArray());
    }

    private static IEnumerable<IArchitectureAnalyzer> GetEnabledAnalyzers(ArchitectureAnalyzerOptions options)
    {
        if (options.Srp)
        {
            yield return SRP_ANALYZER;
        }

        if (options.Dip)
        {
            yield return DIP_ANALYZER;
        }

        if (options.Isp)
        {
            yield return ISP_ANALYZER;
        }

        if (options.OcpLsp)
        {
            yield return OCP_LSP_ANALYZER;
        }

        if (options.Dry)
        {
            yield return DRY_ANALYZER;
        }
    }

    /// <summary>
    /// Resolves candidate .cs files from the diff or by scanning the workspace.
    /// </summary>
    /// <param name="diff">The current diff snapshot.</param>
    /// <param name="workspaceRoot">The workspace root path.</param>
    /// <returns>A list of absolute file paths.</returns>
    internal static List<string> ResolveCandidateFiles(string diff, string workspaceRoot)
    {
        List<string> output = new List<string>();
        string normalizedRoot = CandidateFileResolver.NormalizeRoot(workspaceRoot);
        foreach (string line in CandidateFileResolver.SplitDiffLines(diff))
        {
            if (!line.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (CandidateFileResolver.IsExcludedDirectory(line))
            {
                continue;
            }

            string? resolved = CandidateFileResolver.TryResolve(line, workspaceRoot, normalizedRoot);
            if (resolved is not null)
            {
                output.Add(resolved);
            }
        }

        if (output.Count > 0)
        {
            return output;
        }

        return Directory.GetFiles(workspaceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !CandidateFileResolver.IsExcludedDirectory(f))
            .ToList();
    }

    private static List<ParsedFile> ParseFiles(IEnumerable<string> files)
    {
        List<ParsedFile> parsed = new List<ParsedFile>();
        foreach (string file in files)
        {
            string content = File.ReadAllText(file);
            Microsoft.CodeAnalysis.SyntaxTree tree = CSharpSyntaxTree.ParseText(content);
            Microsoft.CodeAnalysis.SyntaxNode root = tree.GetRoot();
            parsed.Add(new ParsedFile(file, root));
        }

        return parsed;
    }
}
