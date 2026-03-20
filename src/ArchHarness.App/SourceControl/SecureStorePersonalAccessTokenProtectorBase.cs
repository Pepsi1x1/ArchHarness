using ArchHarness.App.Core;

namespace ArchHarness.App.SourceControl;

/// <summary>
/// Shared helper base for secure-store-backed personal access token protectors.
/// </summary>
public abstract class SecureStorePersonalAccessTokenProtectorBase : IPersonalAccessTokenProtector
{
    private readonly string _storeKind;
    private readonly string _unavailableReason;

    /// <summary>
    /// Initializes a new instance of the <see cref="SecureStorePersonalAccessTokenProtectorBase"/> class.
    /// </summary>
    protected SecureStorePersonalAccessTokenProtectorBase(string storeKind, string unavailableReason)
    {
        this._storeKind = storeKind;
        this._unavailableReason = unavailableReason;
    }

    /// <inheritdoc />
    public abstract bool CanProtect { get; }

    /// <inheritdoc />
    public string? UnavailableReason => this.CanProtect ? null : this._unavailableReason;

    /// <inheritdoc />
    public abstract string Protect(string personalAccessToken, string? existingProtectedPersonalAccessToken = null);

    /// <inheritdoc />
    public abstract string Unprotect(string protectedPersonalAccessToken);

    /// <summary>
    /// Throws when the current platform-specific secure store is unavailable.
    /// </summary>
    protected void EnsureSupported()
    {
        if (!this.CanProtect)
        {
            throw new PlatformNotSupportedException(this.UnavailableReason);
        }
    }

    /// <summary>
    /// Resolves the secure-store secret identifier, reusing an existing reference when possible.
    /// </summary>
    protected string ResolveSecretId(string? existingProtectedPersonalAccessToken)
    {
        if (this.TryParseSecretId(existingProtectedPersonalAccessToken, out string? secretId))
        {
            return secretId!;
        }

        return Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// Parses the secure-store secret identifier from a token reference.
    /// </summary>
    protected string ParseSecretId(string protectedPersonalAccessToken, string invalidReferenceMessage)
    {
        if (this.TryParseSecretId(protectedPersonalAccessToken, out string? secretId))
        {
            return secretId!;
        }

        throw new FormatException(invalidReferenceMessage);
    }

    /// <summary>
    /// Attempts to parse a secure-store secret identifier from a token reference.
    /// </summary>
    protected bool TryParseSecretId(string? protectedPersonalAccessToken, out string? secretId)
    {
        secretId = null;
        if (!SecureStoreTokenReference.TryParse(protectedPersonalAccessToken ?? string.Empty, out string storeKind, out string parsedSecretId)
            || string.IsNullOrWhiteSpace(parsedSecretId)
            || !string.Equals(storeKind, this._storeKind, StringComparison.Ordinal))
        {
            return false;
        }

        secretId = parsedSecretId;
        return true;
    }

    /// <summary>
    /// Creates a redacted failure message from a secure-store command result.
    /// </summary>
    protected static string BuildCommandFailureMessage(string storeName, LocalCommandResult result)
    {
        string errorText = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;
        errorText = string.IsNullOrWhiteSpace(errorText)
            ? $"exit code {result.ExitCode}"
            : Redaction.RedactSecrets(errorText.Trim());
        if (errorText.Length > 240)
        {
            errorText = errorText[..240];
        }

        return $"{storeName} operation failed: {errorText}";
    }

    /// <summary>
    /// Creates a secure-store token reference string for the resolved secret identifier.
    /// </summary>
    protected string CreateTokenReference(string secretId)
        => SecureStoreTokenReference.Create(this._storeKind, secretId);
}
