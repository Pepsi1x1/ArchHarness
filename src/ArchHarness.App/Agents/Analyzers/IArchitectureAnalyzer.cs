using ArchHarness.App.Core;

namespace ArchHarness.App.Agents.Analyzers;

/// <summary>
/// Contract for a static architecture analysis rule that inspects parsed files and reports findings.
/// </summary>
public interface IArchitectureAnalyzer
{
    /// <summary>
    /// Analyzes parsed files and appends findings and required actions.
    /// </summary>
    /// <param name="files">The parsed source files to analyze.</param>
    /// <param name="findings">The collection to append findings to.</param>
    /// <param name="requiredActions">The set to append required remediation actions to.</param>
    void Analyze(IReadOnlyList<ParsedFile> files, List<ArchitectureFinding> findings, HashSet<string> requiredActions);
}
