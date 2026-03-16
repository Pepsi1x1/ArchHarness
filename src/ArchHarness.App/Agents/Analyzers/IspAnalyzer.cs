using ArchHarness.App.Constants;
using ArchHarness.App.Core;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ArchHarness.App.Agents.Analyzers;

/// <summary>
/// Detects interfaces with excessive member counts, indicating ISP violations.
/// </summary>
public sealed class IspAnalyzer : IArchitectureAnalyzer
{
    private const string SEVERITY_MEDIUM = Severities.MEDIUM;
    private const int MAX_INTERFACE_MEMBERS = 12;

    /// <inheritdoc />
    public void Analyze(IReadOnlyList<ParsedFile> files, List<ArchitectureFinding> findings, HashSet<string> requiredActions)
    {
        foreach (ParsedFile file in files)
        {
            foreach (InterfaceDeclarationSyntax iface in file.Root.DescendantNodes().OfType<InterfaceDeclarationSyntax>())
            {
                if (iface.Members.Count > MAX_INTERFACE_MEMBERS)
                {
                    findings.Add(new ArchitectureFinding(
                        SEVERITY_MEDIUM,
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
}
