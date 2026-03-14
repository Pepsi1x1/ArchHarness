using ArchHarness.App.Core;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ArchHarness.App.Agents.Analyzers;

/// <summary>
/// Detects classes with excessive method or member counts, indicating SRP violations.
/// </summary>
public sealed class SrpAnalyzer : IArchitectureAnalyzer
{
    private const string SEVERITY_HIGH = "high";
    private const int MAX_METHOD_COUNT = 15;
    private const int MAX_MEMBER_COUNT = 30;

    /// <inheritdoc />
    public void Analyze(IReadOnlyList<ParsedFile> files, List<ArchitectureFinding> findings, HashSet<string> requiredActions)
    {
        foreach (ParsedFile file in files)
        {
            foreach (ClassDeclarationSyntax cls in file.Root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                int methodCount = cls.Members.OfType<MethodDeclarationSyntax>().Count();
                int memberCount = cls.Members.Count;
                if (methodCount > MAX_METHOD_COUNT || memberCount > MAX_MEMBER_COUNT)
                {
                    findings.Add(new ArchitectureFinding(
                        SEVERITY_HIGH,
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
}
