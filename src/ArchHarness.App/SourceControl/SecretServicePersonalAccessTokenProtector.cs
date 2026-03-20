namespace ArchHarness.App.SourceControl;

/// <summary>
/// Stores personal access tokens in the Linux Secret Service keyring via libsecret.
/// </summary>
public sealed class SecretServicePersonalAccessTokenProtector : IPersonalAccessTokenProtector
{
    private const string COMMAND_NAME = "secret-tool";
    private const string STORE_KIND = "secret-service";
    private const string SERVICE_ATTRIBUTE_NAME = "service";
    private const string SERVICE_ATTRIBUTE_VALUE = "ArchHarness";
    private const string ACCOUNT_ATTRIBUTE_NAME = "account";
    private const string SECRET_LABEL = "ArchHarness Personal Access Token";

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
    public bool CanProtect => this._runtimePlatform.IsLinux && this._commandRunner.IsCommandAvailable(COMMAND_NAME);

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
            COMMAND_NAME,
            new[]
            {
                "store",
                $"--label={SECRET_LABEL}",
                SERVICE_ATTRIBUTE_NAME,
                SERVICE_ATTRIBUTE_VALUE,
                ACCOUNT_ATTRIBUTE_NAME,
                secretId
            },
            personalAccessToken);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(BuildCommandFailureMessage("Secret Service", result));
        }

        return SecureStoreTokenReference.Create(STORE_KIND, secretId);
    }

    /// <inheritdoc />
    public string Unprotect(string protectedPersonalAccessToken)
    {
        EnsureSupported(this.CanProtect, this.UnavailableReason);

        string secretId = ParseSecretId(protectedPersonalAccessToken);
        LocalCommandResult result = this._commandRunner.Run(
            COMMAND_NAME,
            new[]
            {
                "lookup",
                SERVICE_ATTRIBUTE_NAME,
                SERVICE_ATTRIBUTE_VALUE,
                ACCOUNT_ATTRIBUTE_NAME,
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
            || !string.Equals(storeKind, STORE_KIND, StringComparison.Ordinal))
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