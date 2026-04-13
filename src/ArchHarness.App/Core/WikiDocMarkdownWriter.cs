using System.Text.Json;

namespace ArchHarness.App.Core;

/// <summary>
/// Provides markdown and JSON writing operations for the wikidoc workflow.
/// </summary>
public interface IWikiDocMarkdownWriter
{
    /// <summary>Writes markdown content to a file under the specified output root and returns the absolute path.</summary>
    Task<string> WriteMarkdownAsync(string root, string relativePath, string markdown, CancellationToken ct);

    /// <summary>Serializes <paramref name="payload"/> to indented JSON and writes it to <paramref name="path"/>.</summary>
    Task WriteJsonAsync(string path, object payload, CancellationToken ct);

    /// <summary>
    /// Writes each concept page to <c>concepts/{slug}.md</c> under <paramref name="outputRoot"/>,
    /// appends workspace-relative paths to <paramref name="filesTouched"/>, and returns the absolute paths.
    /// </summary>
    Task<string[]> WriteConceptPagesAsync(string scanRoot, string outputRoot, IReadOnlyList<WikiDocConceptPage> conceptPages, List<string> filesTouched, CancellationToken cancellationToken);

    /// <summary>Returns the workspace-relative form of <paramref name="fullPath"/>.</summary>
    string ToWorkspaceRelativePath(string scanRoot, string fullPath);

    /// <summary>
    /// Ensures <paramref name="markdown"/> begins with a heading; prepends <c># {title}</c> if it does not.
    /// </summary>
    string EnsureHeading(string markdown, string title);
}

/// <summary>
/// Default implementation of <see cref="IWikiDocMarkdownWriter"/>.
/// </summary>
public sealed class WikiDocMarkdownWriter : IWikiDocMarkdownWriter
{
    /// <inheritdoc />
    public async Task<string> WriteMarkdownAsync(string root, string relativePath, string markdown, CancellationToken ct)
    {
        string fullPath = GetSafeOutputPath(root, relativePath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(fullPath, markdown, ct).ConfigureAwait(false);
        return fullPath;
    }

    /// <inheritdoc />
    public async Task WriteJsonAsync(string path, object payload, CancellationToken ct)
    {
        string json = JsonSerializer.Serialize(payload, JsonDefaults.INDENTED);
        await File.WriteAllTextAsync(path, json, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string[]> WriteConceptPagesAsync(
        string scanRoot,
        string outputRoot,
        IReadOnlyList<WikiDocConceptPage> conceptPages,
        List<string> filesTouched,
        CancellationToken cancellationToken)
    {
        Dictionary<string, int> slugCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        List<string> paths = new List<string>();
        foreach (WikiDocConceptPage conceptPage in conceptPages)
        {
            string slug = NormalizeSlug(conceptPage);
            if (slugCounts.TryGetValue(slug, out int existingCount))
            {
                existingCount++;
                slugCounts[slug] = existingCount;
                slug = $"{slug}-{existingCount}";
            }
            else
            {
                slugCounts[slug] = 1;
            }

            string relativePath = Path.Combine("concepts", $"{slug}.md");
            string conceptPath = await this.WriteMarkdownAsync(
                outputRoot,
                relativePath,
                EnsureHeading(conceptPage.Markdown, conceptPage.Title),
                cancellationToken).ConfigureAwait(false);
            filesTouched.Add(ToWorkspaceRelativePath(scanRoot, conceptPath));
            paths.Add(conceptPath);
        }

        return paths.ToArray();
    }

    /// <inheritdoc />
    public string ToWorkspaceRelativePath(string scanRoot, string fullPath)
        => Path.GetRelativePath(scanRoot, fullPath);

    /// <inheritdoc />
    public string EnsureHeading(string markdown, string title)
    {
        string trimmed = markdown.Trim();
        return trimmed.StartsWith('#')
            ? trimmed
            : $"# {title}{Environment.NewLine}{Environment.NewLine}{trimmed}";
    }

    private static string GetSafeOutputPath(string root, string relativePath)
    {
        string fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!fullPath.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"WikiDoc attempted to write outside the output root: {relativePath}");
        }

        return fullPath;
    }

    private static string NormalizeSlug(WikiDocConceptPage conceptPage)
    {
        string slugSource = string.IsNullOrWhiteSpace(conceptPage.Slug)
            ? conceptPage.Title
            : conceptPage.Slug;
        string normalized = new string(slugSource
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray());
        normalized = string.Join("-", normalized.Split('-', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(normalized) ? "concept" : normalized;
    }
}
