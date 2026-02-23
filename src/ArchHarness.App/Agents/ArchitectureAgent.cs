using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ArchHarness.App.Agents;

public sealed class ArchitectureAgent : AgentBase
{
    private const string SeverityHigh = "high";
    private const string SeverityMedium = "medium";
    private const string ArchitectureInstructions = """
        You are the Architecture Agent.
        Enforce SOLID, structural cohesion, separation of concerns, and DRY by directly editing files.
        Run in agent mode and use built-in tools to make required architecture changes.
        Keep changes inside WorkspaceRoot and update tests when behavior changes.
        Return a concise completion summary after applying changes.
        """;

    public ArchitectureAgent(ICopilotClient copilotClient, IModelResolver modelResolver)
        : base(copilotClient, modelResolver, "architecture") { }

    public async Task<ArchitectureReview> ReviewAsync(
        string delegatedPrompt,
        string diff,
        string workspaceRoot,
        IReadOnlyList<string> filesTouched,
        IDictionary<string, string>? modelOverrides,
        CancellationToken cancellationToken = default)
    {
        var model = ResolveModel(modelOverrides);
        var enforcementPrompt = BuildEnforcementPrompt(delegatedPrompt, workspaceRoot, filesTouched, diff);
        _ = await CopilotClient.CompleteAsync(model, enforcementPrompt, cancellationToken);

        var findings = new List<ArchitectureFinding>();
        var requiredActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (diff.Contains("TODO", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new ArchitectureFinding(SeverityHigh, "Completeness", filesTouched.FirstOrDefault(), "TODO", "TODO marker found in implementation."));
            requiredActions.Add("Remove TODO markers and complete implementation details.");
        }

        var candidateFiles = ResolveCandidateFiles(diff, workspaceRoot);
        if (candidateFiles.Count == 0)
        {
            return new ArchitectureReview(findings, requiredActions.ToArray());
        }

        var parsedFiles = ParseFiles(candidateFiles);
        AnalyzeSingleResponsibility(parsedFiles, findings, requiredActions);
        AnalyzeDependencyInversion(parsedFiles, findings, requiredActions);
        AnalyzeInterfaceSegregation(parsedFiles, findings, requiredActions);
        AnalyzeOpenClosedAndLiskov(parsedFiles, findings, requiredActions);
        AnalyzeDry(parsedFiles, findings, requiredActions);

        var hasTests = filesTouched.Any(f => f.Contains("test", StringComparison.OrdinalIgnoreCase))
            || candidateFiles.Any(f => f.Contains("test", StringComparison.OrdinalIgnoreCase));
        if (!hasTests && candidateFiles.Any(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            findings.Add(new ArchitectureFinding(
                SeverityMedium,
                "SeparationOfConcerns",
                Path.GetRelativePath(workspaceRoot, candidateFiles[0]),
                "Tests",
                "Code changes were detected without corresponding tests."
            ));
            requiredActions.Add("Add or update tests that cover the implemented behavior.");
        }

        return new ArchitectureReview(findings, requiredActions.ToArray());
    }

    private static string BuildEnforcementPrompt(
        string delegatedPrompt,
        string workspaceRoot,
        IReadOnlyList<string> filesTouched,
        string diff)
    {
        var touched = filesTouched.Count == 0 ? "(none)" : string.Join(", ", filesTouched);
        var diffPreview = diff.Length <= 4000 ? diff : diff[..4000];

        return string.Join(Environment.NewLine,
        [
            ArchitectureInstructions,
            string.Empty,
            $"WorkspaceRoot: {workspaceRoot}",
            "Write boundaries: Do not modify outside WorkspaceRoot.",
            string.Empty,
            "DelegatedPrompt:",
            delegatedPrompt,
            string.Empty,
            $"FilesTouched: {touched}",
            "CurrentDiffSnapshot:",
            diffPreview
        ]);
    }

    private static List<string> ResolveCandidateFiles(string diff, string workspaceRoot)
    {
        var output = new List<string>();
        var lines = diff.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
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

            var fullPath = Path.GetFullPath(Path.Combine(workspaceRoot, line));
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
        var parsed = new List<ParsedFile>();
        foreach (var file in files)
        {
            var content = File.ReadAllText(file);
            var tree = CSharpSyntaxTree.ParseText(content);
            var root = tree.GetRoot();
            parsed.Add(new ParsedFile(file, root));
        }

        return parsed;
    }

    private static void AnalyzeSingleResponsibility(List<ParsedFile> files, List<ArchitectureFinding> findings, HashSet<string> requiredActions)
    {
        foreach (var file in files)
        {
            foreach (var cls in file.Root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                var methodCount = cls.Members.OfType<MethodDeclarationSyntax>().Count();
                var memberCount = cls.Members.Count;
                if (methodCount > 15 || memberCount > 30)
                {
                    findings.Add(new ArchitectureFinding(
                        SeverityHigh,
                        "SOLID-SRP",
                        file.RelativePath,
                        cls.Identifier.Text,
                        $"Class has high complexity ({methodCount} methods, {memberCount} members), indicating multiple responsibilities."
                    ));
                    requiredActions.Add("Split high-complexity classes into smaller cohesive units with focused responsibilities.");
                }
            }
        }
    }

    private static void AnalyzeDependencyInversion(List<ParsedFile> files, List<ArchitectureFinding> findings, HashSet<string> requiredActions)
    {
        foreach (var file in files)
        {
            foreach (var ctor in file.Root.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
            {
                var parameterCount = ctor.ParameterList.Parameters.Count;
                if (parameterCount > 6)
                {
                    findings.Add(new ArchitectureFinding(
                        SeverityMedium,
                        "SOLID-DIP",
                        file.RelativePath,
                        ctor.Identifier.Text,
                        $"Constructor injects {parameterCount} dependencies, which may indicate orchestration leakage and low cohesion."
                    ));
                    requiredActions.Add("Refactor constructor dependencies using focused collaborators or facades to preserve cohesion.");
                }
            }
        }
    }

    private static void AnalyzeInterfaceSegregation(List<ParsedFile> files, List<ArchitectureFinding> findings, HashSet<string> requiredActions)
    {
        foreach (var file in files)
        {
            foreach (var iface in file.Root.DescendantNodes().OfType<InterfaceDeclarationSyntax>())
            {
                if (iface.Members.Count > 12)
                {
                    findings.Add(new ArchitectureFinding(
                        SeverityMedium,
                        "SOLID-ISP",
                        file.RelativePath,
                        iface.Identifier.Text,
                        $"Interface exposes {iface.Members.Count} members and may force clients to depend on unused methods."
                    ));
                    requiredActions.Add("Split broad interfaces into role-focused contracts to improve interface segregation.");
                }
            }
        }
    }

    private static void AnalyzeOpenClosedAndLiskov(List<ParsedFile> files, List<ArchitectureFinding> findings, HashSet<string> requiredActions)
    {
        foreach (var file in files)
        {
            foreach (var switchStmt in file.Root.DescendantNodes().OfType<SwitchStatementSyntax>().Where(s => s.Sections.Count >= 6))
            {
                findings.Add(new ArchitectureFinding(
                    SeverityMedium,
                    "SOLID-OCP",
                    file.RelativePath,
                    "switch",
                    $"Large switch statement with {switchStmt.Sections.Count} branches may require modification for each new case."
                ));
                requiredActions.Add("Replace large switch conditionals with polymorphism or strategy mappings where appropriate.");
            }

            foreach (var method in file.Root.DescendantNodes().OfType<MethodDeclarationSyntax>().Where(m => m.Modifiers.Any(SyntaxKind.NewKeyword)))
            {
                findings.Add(new ArchitectureFinding(
                    SeverityMedium,
                    "SOLID-LSP",
                    file.RelativePath,
                    method.Identifier.Text,
                    "Method hides a base member using 'new', which may violate substitutability expectations."
                ));
                requiredActions.Add("Avoid member hiding in inheritance hierarchies; prefer virtual/override semantics or composition.");
            }
        }
    }

    private static void AnalyzeDry(List<ParsedFile> files, List<ArchitectureFinding> findings, HashSet<string> requiredActions)
    {
        var bodyMap = new Dictionary<string, List<(string File, string Symbol)>>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            foreach (var method in file.Root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                var normalized = NormalizeMethodBody(method);
                if (string.IsNullOrWhiteSpace(normalized) || normalized.Length < 120)
                {
                    continue;
                }

                if (!bodyMap.TryGetValue(normalized, out var locations))
                {
                    locations = new List<(string, string)>();
                    bodyMap[normalized] = locations;
                }

                locations.Add((file.RelativePath, method.Identifier.Text));
            }
        }

        foreach (var locations in bodyMap.Values.Where(v => v.Count > 1))
        {
            var first = locations[0];
            var duplicates = string.Join(", ", locations.Skip(1).Select(x => $"{x.File}:{x.Symbol}"));
            findings.Add(new ArchitectureFinding(
                SeverityHigh,
                "DRY",
                first.File,
                first.Symbol,
                $"Duplicated method logic detected across: {duplicates}."
            ));
            requiredActions.Add("Extract duplicated logic into shared methods/components to enforce DRY.");
        }
    }

    private static string NormalizeMethodBody(MethodDeclarationSyntax method)
    {
        var body = method.Body?.ToFullString() ?? method.ExpressionBody?.ToFullString();
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        return string.Concat(body.Where(c => !char.IsWhiteSpace(c)));
    }

    private sealed record ParsedFile(string Path, SyntaxNode Root)
    {
        public string RelativePath => System.IO.Path.GetRelativePath(Directory.GetCurrentDirectory(), Path);
    }
}
