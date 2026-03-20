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
            ?? "Secure personal access token storage is not available on this platform. Saving a personal access token requires a supported secure store.";

    /// <inheritdoc />
    public Task<string> ProtectAsync(string personalAccessToken, string? existingProtectedPersonalAccessToken = null)
    {
        if (!this.CanProtect)
        {
            throw new PlatformNotSupportedException(this.UnavailableReason);
        }

        return this._activeProtector!.ProtectAsync(personalAccessToken, existingProtectedPersonalAccessToken);
    }

    /// <inheritdoc />
    public Task<string> UnprotectAsync(string protectedPersonalAccessToken)
    {
        if (!this.CanProtect)
        {
            throw new PlatformNotSupportedException(this.UnavailableReason);
        }

        return this._activeProtector!.UnprotectAsync(protectedPersonalAccessToken);
    }
}
