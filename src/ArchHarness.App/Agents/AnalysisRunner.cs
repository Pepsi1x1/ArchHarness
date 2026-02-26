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

    private static readonly IReadOnlyList<IArchitectureAnalyzer> Analyzers = new IArchitectureAnalyzer[]
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
        foreach (IArchitectureAnalyzer analyzer in Analyzers)
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
        string[] lines = diff.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string line in lines)
        {
            if (!line.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (line.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("/obj/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string fullPath = Path.GetFullPath(Path.Combine(workspaceRoot, line));
            if (File.Exists(fullPath))
            {
                output.Add(fullPath);
            }
        }

        if (output.Count > 0)
        {
            return output;
        }

        return Directory.GetFiles(workspaceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase)
                     && !f.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase))
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
