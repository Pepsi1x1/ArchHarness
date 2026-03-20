using ArchHarness.App.Core;

namespace ArchHarness.App.SourceControl;

/// <summary>
/// Stores personal access tokens in the macOS login keychain.
/// </summary>
public sealed class KeychainPersonalAccessTokenProtector : SecureStorePersonalAccessTokenProtectorBase
{
    private const string COMMAND_NAME = "security";
    private const string STORE_KIND = "keychain";
    private const string SERVICE_NAME = "ArchHarness.PersonalAccessToken";

    private readonly ILocalCommandRunner _commandRunner;
    private readonly IRuntimePlatform _runtimePlatform;

    /// <summary>
    /// Initializes a new instance of the <see cref="KeychainPersonalAccessTokenProtector"/> class.
    /// </summary>
    public KeychainPersonalAccessTokenProtector(ILocalCommandRunner commandRunner, IRuntimePlatform runtimePlatform)
        : base(STORE_KIND, "macOS Keychain storage requires the 'security' command and a macOS runtime.")
    {
        this._commandRunner = commandRunner;
        this._runtimePlatform = runtimePlatform;
    }

    /// <inheritdoc />
    public override bool CanProtect => this._runtimePlatform.IsMacOS && this._commandRunner.IsCommandAvailable(COMMAND_NAME);

    /// <inheritdoc />
    public override string Protect(string personalAccessToken, string? existingProtectedPersonalAccessToken = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(personalAccessToken);
        this.EnsureSupported();

        string secretId = this.ResolveSecretId(existingProtectedPersonalAccessToken);
        LocalCommandResult result = this._commandRunner.Run(
            COMMAND_NAME,
            new[]
            {
                "add-generic-password",
                "-U",
                "-a",
                secretId,
                "-s",
                SERVICE_NAME,
                "-w",
                personalAccessToken
            });

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(BuildCommandFailureMessage("macOS Keychain", result));
        }

        return this.CreateTokenReference(secretId);
    }

    /// <inheritdoc />
    public override string Unprotect(string protectedPersonalAccessToken)
    {
        this.EnsureSupported();

        string secretId = this.ParseSecretId(protectedPersonalAccessToken, "The stored macOS Keychain token reference is invalid.");
        LocalCommandResult result = this._commandRunner.Run(
            COMMAND_NAME,
            new[]
            {
                "find-generic-password",
                "-a",
                secretId,
                "-s",
                SERVICE_NAME,
                "-w"
            });

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(BuildCommandFailureMessage("macOS Keychain", result));
        }

        return result.StandardOutput.TrimEnd('\r', '\n');
    }
}

