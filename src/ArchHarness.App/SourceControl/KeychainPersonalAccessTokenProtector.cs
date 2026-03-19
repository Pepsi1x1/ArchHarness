namespace ArchHarness.App.SourceControl;

/// <summary>
/// Stores personal access tokens in the macOS login keychain.
/// </summary>
public sealed class KeychainPersonalAccessTokenProtector : IPersonalAccessTokenProtector
{
    private const string CommandName = "security";
    private const string StoreKind = "keychain";
    private const string ServiceName = "ArchHarness.PersonalAccessToken";

    private readonly ILocalCommandRunner _commandRunner;
    private readonly IRuntimePlatform _runtimePlatform;

    /// <summary>
    /// Initializes a new instance of the <see cref="KeychainPersonalAccessTokenProtector"/> class.
    /// </summary>
    public KeychainPersonalAccessTokenProtector(ILocalCommandRunner commandRunner, IRuntimePlatform runtimePlatform)
    {
        this._commandRunner = commandRunner;
        this._runtimePlatform = runtimePlatform;
    }

    /// <inheritdoc />
    public bool CanProtect => this._runtimePlatform.IsMacOS && this._commandRunner.IsCommandAvailable(CommandName);

    /// <inheritdoc />
    public string? UnavailableReason => this.CanProtect
        ? null
        : "macOS Keychain storage requires the 'security' command and a macOS runtime.";

    /// <inheritdoc />
    public string Protect(string personalAccessToken, string? existingProtectedPersonalAccessToken = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(personalAccessToken);

        EnsureSupported(this.CanProtect, this.UnavailableReason);

        string secretId = ResolveSecretId(existingProtectedPersonalAccessToken);
        LocalCommandResult result = this._commandRunner.Run(
            CommandName,
            new[]
            {
                "add-generic-password",
                "-U",
                "-a",
                secretId,
                "-s",
                ServiceName,
                "-w",
                personalAccessToken
            });

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(BuildCommandFailureMessage("macOS Keychain", result));
        }

        return SecureStoreTokenReference.Create(StoreKind, secretId);
    }

    /// <inheritdoc />
    public string Unprotect(string protectedPersonalAccessToken)
    {
        EnsureSupported(this.CanProtect, this.UnavailableReason);

        string secretId = ParseSecretId(protectedPersonalAccessToken);
        LocalCommandResult result = this._commandRunner.Run(
            CommandName,
            new[]
            {
                "find-generic-password",
                "-a",
                secretId,
                "-s",
                ServiceName,
                "-w"
            });

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(BuildCommandFailureMessage("macOS Keychain", result));
        }

        return result.StandardOutput.TrimEnd('\r', '\n');
    }

    private static string ResolveSecretId(string? existingProtectedPersonalAccessToken)
    {
        if (TryParseSecretId(existingProtectedPersonalAccessToken, out string? secretId))
        {
            return secretId;
        }

        return Guid.NewGuid().ToString("N");
    }

    private static string ParseSecretId(string protectedPersonalAccessToken)
        => TryParseSecretId(protectedPersonalAccessToken, out string? secretId)
            ? secretId
            : throw new FormatException("The stored macOS Keychain token reference is invalid.");

    private static bool TryParseSecretId(string? protectedPersonalAccessToken, out string? secretId)
    {
        secretId = null;
        if (!SecureStoreTokenReference.TryParse(protectedPersonalAccessToken ?? string.Empty, out string storeKind, out string parsedSecretId)
            || !string.Equals(storeKind, StoreKind, StringComparison.Ordinal))
        {
            return false;
        }

        secretId = parsedSecretId;
        return true;
    }

    private static void EnsureSupported(bool canProtect, string? unavailableReason)
    {
        if (!canProtect)
        {
            throw new PlatformNotSupportedException(unavailableReason);
        }
    }

    private static string BuildCommandFailureMessage(string storeName, LocalCommandResult result)
    {
        string errorText = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;
        errorText = string.IsNullOrWhiteSpace(errorText) ? $"exit code {result.ExitCode}" : errorText.Trim();
        return $"{storeName} operation failed: {errorText}";
    }
}