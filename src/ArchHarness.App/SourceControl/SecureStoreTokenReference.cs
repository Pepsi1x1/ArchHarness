namespace ArchHarness.App.SourceControl;

/// <summary>
/// Encodes references to secrets stored in platform-native credential stores.
/// </summary>
internal static class SecureStoreTokenReference
{
    private const string PREFIX = "secure-store:";

    public static string Create(string storeKind, string secretId)
        => $"{PREFIX}{storeKind}:{secretId}";

    public static bool TryParse(string protectedPersonalAccessToken, out string storeKind, out string secretId)
    {
        storeKind = string.Empty;
        secretId = string.Empty;

        if (string.IsNullOrWhiteSpace(protectedPersonalAccessToken)
            || !protectedPersonalAccessToken.StartsWith(PREFIX, StringComparison.Ordinal))
        {
            return false;
        }

        string remainder = protectedPersonalAccessToken[PREFIX.Length..];
        int separatorIndex = remainder.IndexOf(':');
        if (separatorIndex <= 0 || separatorIndex == remainder.Length - 1)
        {
            return false;
        }

        storeKind = remainder[..separatorIndex];
        secretId = remainder[(separatorIndex + 1)..];
        return !string.IsNullOrWhiteSpace(storeKind) && !string.IsNullOrWhiteSpace(secretId);
    }
}
