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
    public string RelativePath => System.IO.Path.GetRelativePath(Directory.GetCurrentDirectory(), this.Path);
}
