#pragma warning disable S2325 // DI-injectable sealed class; instance method is correct for the abstraction boundary even without current instance state.

namespace ArchHarness.App.Core;

/// <summary>
/// Resolves the output root for wiki documentation, applying rename or fallback strategies as needed.
/// </summary>
public sealed class WikiDocOutputResolver
{
    private static readonly HashSet<string> AllowedDocumentationExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        string.Empty,
        ".drawio",
        ".gif",
        ".jpeg",
        ".jpg",
        ".json",
        ".markdown",
        ".md",
        ".mdx",
        ".pdf",
        ".png",
        ".svg",
        ".txt",
        ".webp",
        ".yaml",
        ".yml"
    };

    /// <summary>
    /// Resolves the output root for a wiki under <paramref name="ownerRoot"/>,
    /// falling back to <paramref name="fallbackBaseRoot"/> if the local path cannot be used.
    /// </summary>
    public WikiDocOutputResolution Resolve(string ownerRoot, string fallbackBaseRoot)
    {
        string wikiRoot = Path.Combine(ownerRoot, "wiki");
        if (Directory.Exists(wikiRoot))
        {
            return new WikiDocOutputResolution(wikiRoot, wikiRoot, false, null, false, null, null, null);
        }

        if (File.Exists(wikiRoot))
        {
            return CreateFallbackResolution(wikiRoot, fallbackBaseRoot, null, false, "wiki-path-is-file", "A file already exists at the repository-local wiki path.");
        }

        string? renameCandidate = FindDocumentationRenameCandidate(ownerRoot);
        bool renameCandidateWasEligible = !string.IsNullOrWhiteSpace(renameCandidate) && CanSafelyRenameDocumentationFolder(renameCandidate);
        if (renameCandidateWasEligible)
        {
            string safeRenameCandidate = renameCandidate!;
            try
            {
                Directory.Move(safeRenameCandidate, wikiRoot);
                return new WikiDocOutputResolution(wikiRoot, wikiRoot, false, safeRenameCandidate, true, safeRenameCandidate, null, null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Continue below and try to create a fresh wiki directory.
            }
        }

        try
        {
            Directory.CreateDirectory(wikiRoot);
            return new WikiDocOutputResolution(wikiRoot, wikiRoot, false, renameCandidate, renameCandidateWasEligible, null, null, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return CreateFallbackResolution(wikiRoot, fallbackBaseRoot, renameCandidate, renameCandidateWasEligible, "wiki-directory-create-failed", ex.Message);
        }
    }

    private static WikiDocOutputResolution CreateFallbackResolution(string requestedLocalRoot, string fallbackBaseRoot, string? renameCandidate, bool renameCandidateWasEligible, string reasonCode, string reason)
    {
        string outputRoot = Path.Combine(fallbackBaseRoot, "wiki");
        Directory.CreateDirectory(outputRoot);
        return new WikiDocOutputResolution(outputRoot, requestedLocalRoot, true, renameCandidate, renameCandidateWasEligible, null, reasonCode, reason);
    }

    private static string? FindDocumentationRenameCandidate(string ownerRoot)
    {
        string[] candidates = { "docs", "doc", "documentation" };
        string? match = candidates
            .Select(candidate => Path.Combine(ownerRoot, candidate))
            .FirstOrDefault(Directory.Exists);
        return match;
    }

    private static bool CanSafelyRenameDocumentationFolder(string candidateRoot)
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(candidateRoot, "*", SearchOption.AllDirectories))
            {
                string extension = Path.GetExtension(file);
                if (!AllowedDocumentationExtensions.Contains(extension))
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
