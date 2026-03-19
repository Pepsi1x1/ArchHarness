namespace ArchHarness.App.SourceControl;

/// <summary>
/// Stores personal access tokens in the Linux Secret Service keyring via libsecret.
/// </summary>
public sealed class SecretServicePersonalAccessTokenProtector : IPersonalAccessTokenProtector
{
    private const string CommandName = "secret-tool";
    private const string StoreKind = "secret-service";
    private const string ServiceAttributeName = "service";
    private const string ServiceAttributeValue = "ArchHarness";
    private const string AccountAttributeName = "account";
    private const string SecretLabel = "ArchHarness Personal Access Token";

    private readonly ILocalCommandRunner _commandRunner;
    private readonly IRuntimePlatform _runtimePlatform;

    /// <summary>
    /// Initializes a new instance of the <see cref="SecretServicePersonalAccessTokenProtector"/> class.
    /// </summary>
    public SecretServicePersonalAccessTokenProtector(ILocalCommandRunner commandRunner, IRuntimePlatform runtimePlatform)
    {
        this._commandRunner = commandRunner;
        this._runtimePlatform = runtimePlatform;
    }

    /// <inheritdoc />
    public bool CanProtect => this._runtimePlatform.IsLinux && this._commandRunner.IsCommandAvailable(CommandName);

    /// <inheritdoc />
    public string? UnavailableReason => this.CanProtect
        ? null
        : "Linux Secret Service storage requires the 'secret-tool' command and a Secret Service-compatible keyring.";

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
                "store",
                $"--label={SecretLabel}",
                ServiceAttributeName,
                ServiceAttributeValue,
                AccountAttributeName,
                secretId
            },
            personalAccessToken);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(BuildCommandFailureMessage("Secret Service", result));
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
                "lookup",
                ServiceAttributeName,
                ServiceAttributeValue,
                AccountAttributeName,
                secretId
            });

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(BuildCommandFailureMessage("Secret Service", result));
        }

        return result.StandardOutput.TrimEnd('\r', '\n');
    }

    private static string ResolveSecretId(string? existingProtectedPersonalAccessToken)
    {
        if (TryParseSecretId(existingProtectedPersonalAccessToken, out string? secretId))
        {
            return secretId!;
        }

        return Guid.NewGuid().ToString("N");
    }

    private static string ParseSecretId(string protectedPersonalAccessToken)
    {
        if (TryParseSecretId(protectedPersonalAccessToken, out string? secretId))
        {
            return secretId!;
        }

        throw new FormatException("The stored Secret Service token reference is invalid.");
    }

    private static bool TryParseSecretId(string? protectedPersonalAccessToken, out string? secretId)
    {
        secretId = null;
        if (!SecureStoreTokenReference.TryParse(protectedPersonalAccessToken ?? string.Empty, out string storeKind, out string parsedSecretId)
            || string.IsNullOrWhiteSpace(parsedSecretId)
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