using ArchHarness.App.Core;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ArchHarness.App.Agents.Analyzers;

/// <summary>
/// Detects constructors with excessive dependency counts, indicating DIP violations.
/// </summary>
public sealed class DipAnalyzer : IArchitectureAnalyzer
{
    private const string SEVERITY_MEDIUM = "medium";

    /// <inheritdoc />
    public void Analyze(IReadOnlyList<ParsedFile> files, List<ArchitectureFinding> findings, HashSet<string> requiredActions)
    {
        foreach (ParsedFile file in files)
        {
            foreach (ConstructorDeclarationSyntax ctor in file.Root.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
            {
                int parameterCount = ctor.ParameterList.Parameters.Count;
                if (parameterCount > 6)
                {
                    findings.Add(new ArchitectureFinding(
                        SEVERITY_MEDIUM,
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
}
