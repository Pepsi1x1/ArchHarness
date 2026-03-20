using System.Security.Cryptography;
using System.Text;

namespace ArchHarness.App.SourceControl;

/// <summary>
/// Encrypts personal access tokens using the current Windows user profile.
/// </summary>
public sealed class DpapiPersonalAccessTokenProtector : IPersonalAccessTokenProtector
{
    private const string UNAVAILABLE_REASON = "DPAPI token protection is only available on Windows.";

    /// <inheritdoc />
    public bool CanProtect => OperatingSystem.IsWindows();

    /// <inheritdoc />
    public string? UnavailableReason => this.CanProtect ? null : UNAVAILABLE_REASON;

    /// <inheritdoc />
    public string Protect(string personalAccessToken, string? existingProtectedPersonalAccessToken = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(UNAVAILABLE_REASON);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(personalAccessToken);

        byte[] plaintext = Encoding.UTF8.GetBytes(personalAccessToken);
        byte[] protectedBytes = ProtectedData.Protect(plaintext, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    /// <inheritdoc />
    public string Unprotect(string protectedPersonalAccessToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(UNAVAILABLE_REASON);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(protectedPersonalAccessToken);

        byte[] protectedBytes = Convert.FromBase64String(protectedPersonalAccessToken);
        byte[] plaintext = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plaintext);
    }
}
