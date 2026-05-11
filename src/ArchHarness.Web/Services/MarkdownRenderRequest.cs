namespace ArchHarness.Web.Services;

/// <summary>
/// Represents a browser markdown payload that should be rendered to HTML by the local web host.
/// </summary>
/// <param name="Markdown">The markdown content to render.</param>
public sealed record MarkdownRenderRequest(string Markdown);
