using ArchHarness.App.Core;
using Microsoft.CodeAnalysis;

namespace ArchHarness.App.Agents.Analyzers;

/// <summary>
/// A parsed C# source file used for architecture analysis.
/// </summary>
/// <param name="Path">The absolute file path.</param>
/// <param name="Root">The parsed syntax tree root node.</param>
public sealed record ParsedFile(string Path, SyntaxNode Root)
{
    /// <summary>
    /// Returns the file path relative to the current directory.
    /// </summary>
    public string RelativePath => System.IO.Path.GetRelativePath(Directory.GetCurrentDirectory(), Path);
}

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
