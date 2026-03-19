namespace ArchHarness.App.SourceControl;

/// <summary>
/// Uses the best available built-in token protection for the current platform.
/// </summary>
public sealed class PlatformPersonalAccessTokenProtector : IPersonalAccessTokenProtector
{
    private readonly IPersonalAccessTokenProtector? _activeProtector;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlatformPersonalAccessTokenProtector"/> class.
    /// </summary>
    public PlatformPersonalAccessTokenProtector(ILocalCommandRunner commandRunner, IRuntimePlatform runtimePlatform)
    {
        if (runtimePlatform.IsWindows)
        {
            this._activeProtector = new DpapiPersonalAccessTokenProtector();
        }
        else if (runtimePlatform.IsMacOS)
        {
            this._activeProtector = new KeychainPersonalAccessTokenProtector(commandRunner, runtimePlatform);
        }
        else if (runtimePlatform.IsLinux)
        {
            this._activeProtector = new SecretServicePersonalAccessTokenProtector(commandRunner, runtimePlatform);
        }
    }

    /// <inheritdoc />
    public bool CanProtect => this._activeProtector?.CanProtect == true;

    /// <inheritdoc />
    public string? UnavailableReason => this.CanProtect
        ? null
        : this._activeProtector?.UnavailableReason
            ?? "Secure personal access token storage is not available on this platform. Storing the token will write it to disk in plain text.";

    /// <inheritdoc />
    public string Protect(string personalAccessToken, string? existingProtectedPersonalAccessToken = null)
    {
        if (!this.CanProtect)
        {
            throw new PlatformNotSupportedException(this.UnavailableReason);
        }

        return this._activeProtector!.Protect(personalAccessToken, existingProtectedPersonalAccessToken);
    }

    /// <inheritdoc />
    public string Unprotect(string protectedPersonalAccessToken)
    {
        if (!this.CanProtect)
        {
            throw new PlatformNotSupportedException(this.UnavailableReason);
        }

        return this._activeProtector!.Unprotect(protectedPersonalAccessToken);
    }
}