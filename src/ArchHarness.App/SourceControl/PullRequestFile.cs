namespace ArchHarness.App.SourceControl;

public record PullRequestFile(string Path, string ChangeType);

internal static class PullRequestFileChangeTypes
{
    public const string ADDED = "Added";
    public const string MODIFIED = "Modified";
    public const string DELETED = "Deleted";
    public const string RENAMED = "Renamed";

    public static string Normalize(string providerChangeType)
    {
        if (string.IsNullOrWhiteSpace(providerChangeType))
        {
            throw new InvalidOperationException("The provider did not return a valid pull request file change type.");
        }

        string[] segments = providerChangeType.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string segment in segments)
        {
            string? normalized = NormalizeSingle(segment);
            if (normalized is not null)
            {
                return normalized;
            }
        }

        string? singleValue = NormalizeSingle(providerChangeType);
        if (singleValue is not null)
        {
            return singleValue;
        }

        throw new InvalidOperationException($"Unsupported pull request file change type '{providerChangeType}'.");
    }

    private static string? NormalizeSingle(string providerChangeType)
        => providerChangeType.Trim().ToLowerInvariant() switch
        {
            "add" or "added" => ADDED,
            "edit" or "modify" or "modified" or "change" or "changed" or "copy" or "copied" => MODIFIED,
            "delete" or "deleted" or "remove" or "removed" => DELETED,
            "rename" or "renamed" or "sourcerename" or "targetrename" => RENAMED,
            _ => null
        };
}
