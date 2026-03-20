using ArchHarness.App.Core;

namespace ArchHarness.App.SourceControl;

/// <summary>
/// Stores personal access tokens in the Linux Secret Service keyring via libsecret.
/// </summary>
public sealed class SecretServicePersonalAccessTokenProtector : SecureStorePersonalAccessTokenProtectorBase
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
        : base(STORE_KIND, "Linux Secret Service storage requires the 'secret-tool' command and a Secret Service-compatible keyring.")
    {
        this._commandRunner = commandRunner;
        this._runtimePlatform = runtimePlatform;
    }

    /// <inheritdoc />
    public override bool CanProtect => this._runtimePlatform.IsLinux && this._commandRunner.IsCommandAvailable(COMMAND_NAME);

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

        return this.CreateTokenReference(secretId);
    }

    /// <inheritdoc />
    public override string Unprotect(string protectedPersonalAccessToken)
    {
        this.EnsureSupported();

        string secretId = this.ParseSecretId(protectedPersonalAccessToken, "The stored Secret Service token reference is invalid.");
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
}

