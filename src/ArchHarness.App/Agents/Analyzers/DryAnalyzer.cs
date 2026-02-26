using ArchHarness.App.Core;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ArchHarness.App.Agents.Analyzers;

/// <summary>
/// Detects duplicated method bodies across files, indicating DRY violations.
/// </summary>
public sealed class DryAnalyzer : IArchitectureAnalyzer
{
    private const string SEVERITY_HIGH = "high";

    /// <inheritdoc />
    public void Analyze(IReadOnlyList<ParsedFile> files, List<ArchitectureFinding> findings, HashSet<string> requiredActions)
    {
        Dictionary<string, List<(string File, string Symbol)>> bodyMap = new Dictionary<string, List<(string File, string Symbol)>>(StringComparer.Ordinal);

        foreach (ParsedFile file in files)
        {
            foreach (MethodDeclarationSyntax method in file.Root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                string normalized = NormalizeMethodBody(method);
                if (string.IsNullOrWhiteSpace(normalized) || normalized.Length < 120)
                {
                    continue;
                }

                if (!bodyMap.TryGetValue(normalized, out List<(string File, string Symbol)>? locations))
                {
                    locations = new List<(string, string)>();
                    bodyMap[normalized] = locations;
                }

                locations.Add((file.RelativePath, method.Identifier.Text));
            }
        }

        foreach (List<(string File, string Symbol)> locations in bodyMap.Values.Where(v => v.Count > 1))
        {
            (string File, string Symbol) first = locations[0];
            string duplicates = string.Join(", ", locations.Skip(1).Select(x => $"{x.File}:{x.Symbol}"));
            findings.Add(new ArchitectureFinding(
                SEVERITY_HIGH,
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
        string? body = method.Body?.ToFullString() ?? method.ExpressionBody?.ToFullString();
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        return string.Concat(body.Where(c => !char.IsWhiteSpace(c)));
    }
}
