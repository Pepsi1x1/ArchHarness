using ArchHarness.App.Agents.Analyzers;
using ArchHarness.App.Core;
using Microsoft.CodeAnalysis.CSharp;

namespace ArchHarness.App.Agents;

/// <summary>
/// Invokes static architecture analyzers on parsed source files and aggregates findings.
/// </summary>
internal static class AnalysisRunner
{
    private const string SEVERITY_HIGH = "high";
    private const string SEVERITY_MEDIUM = "medium";

    private static readonly IReadOnlyList<IArchitectureAnalyzer> ANALYZERS = new IArchitectureAnalyzer[]
    {
        new SrpAnalyzer(),
        new DipAnalyzer(),
        new IspAnalyzer(),
        new OcpLspAnalyzer(),
        new DryAnalyzer()
    };

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
        IReadOnlyList<string> filesTouched)
    {
        List<ArchitectureFinding> findings = new List<ArchitectureFinding>();
        HashSet<string> requiredActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (diff.Contains("TODO", StringComparison.OrdinalIgnoreCase))
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
        foreach (IArchitectureAnalyzer analyzer in ANALYZERS)
        {
            analyzer.Analyze(parsedFiles, findings, requiredActions);
        }

        bool hasTests = filesTouched.Any(f => f.Contains("test", StringComparison.OrdinalIgnoreCase))
            || candidateFiles.Any(f => f.Contains("test", StringComparison.OrdinalIgnoreCase));
        if (!hasTests && candidateFiles.Any(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
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
