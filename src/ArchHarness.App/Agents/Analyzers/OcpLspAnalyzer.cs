using ArchHarness.App.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ArchHarness.App.Agents.Analyzers;

/// <summary>
/// Detects large switch statements (OCP) and method hiding via 'new' keyword (LSP).
/// </summary>
public sealed class OcpLspAnalyzer : IArchitectureAnalyzer
{
    private const string SEVERITY_MEDIUM = "medium";

    /// <inheritdoc />
    public void Analyze(IReadOnlyList<ParsedFile> files, List<ArchitectureFinding> findings, HashSet<string> requiredActions)
    {
        foreach (ParsedFile file in files)
        {
            foreach (SwitchStatementSyntax switchStmt in file.Root.DescendantNodes().OfType<SwitchStatementSyntax>().Where(s => s.Sections.Count >= 6))
            {
                findings.Add(new ArchitectureFinding(
                    SEVERITY_MEDIUM,
                    "SOLID-OCP",
                    file.RelativePath,
                    "switch",
                    $"Large switch statement with {switchStmt.Sections.Count} branches may require modification for each new case."
                ));
                requiredActions.Add("Replace large switch conditionals with polymorphism or strategy mappings where appropriate.");
            }

            foreach (MethodDeclarationSyntax method in file.Root.DescendantNodes().OfType<MethodDeclarationSyntax>().Where(m => m.Modifiers.Any(SyntaxKind.NewKeyword)))
            {
                findings.Add(new ArchitectureFinding(
                    SEVERITY_MEDIUM,
                    "SOLID-LSP",
                    file.RelativePath,
                    method.Identifier.Text,
                    "Method hides a base member using 'new', which may violate substitutability expectations."
                ));
                requiredActions.Add("Avoid member hiding in inheritance hierarchies; prefer virtual/override semantics or composition.");
            }
        }
    }
}
