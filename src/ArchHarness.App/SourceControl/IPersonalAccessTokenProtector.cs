namespace ArchHarness.App.SourceControl;

/// <summary>
/// Protects personal access tokens before they are written to persistent storage.
/// </summary>
public interface IPersonalAccessTokenProtector
{
    /// <summary>
    /// Gets a value indicating whether secure token protection is available on the current platform.
    /// </summary>
    bool CanProtect { get; }

    /// <summary>
    /// Gets the reason secure token protection is unavailable, or null when protection is supported.
    /// </summary>
    string? UnavailableReason { get; }

    /// <summary>
    /// Encrypts a personal access token for storage.
    /// </summary>
    string Protect(string personalAccessToken, string? existingProtectedPersonalAccessToken = null);

    /// <summary>
    /// Decrypts a previously protected personal access token.
    /// </summary>
    string Unprotect(string protectedPersonalAccessToken);
}
