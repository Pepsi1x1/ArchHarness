using ArchHarness.App.SourceControl;

namespace ArchHarness.App.Tests.Core;

public sealed class NativePersonalAccessTokenProtectorTests
{
    /// <summary>
    /// Verifies that a Keychain protector reuses the existing secret reference when updating a stored token.
    /// </summary>
    [Fact]
    public void KeychainProtector_ReusesExistingReferenceOnUpdate()
    {
        FakeCommandRunner commandRunner = new FakeCommandRunner();
        commandRunner.SetAvailability("security", true);
        commandRunner.OnRun = (command, arguments, standardInput) =>
        {
            Assert.Equal("security", command);
            if (arguments[0] == "add-generic-password")
            {
                Assert.Equal("-U", arguments[1]);
                Assert.Equal("-a", arguments[2]);
                string secretId = arguments[3];
                commandRunner.LastSecretId = secretId;
                Assert.Equal("-s", arguments[4]);
                Assert.Equal("ArchHarness.PersonalAccessToken", arguments[5]);
                Assert.Equal("-w", arguments[6]);
                commandRunner.StoredSecrets[secretId] = arguments[7];
                return new LocalCommandResult(0, string.Empty, string.Empty);
            }

            if (arguments[0] == "find-generic-password")
            {
                string secretId = arguments[2];
                return new LocalCommandResult(0, commandRunner.StoredSecrets[secretId] + Environment.NewLine, string.Empty);
            }

            throw new InvalidOperationException("Unexpected command.");
        };

        KeychainPersonalAccessTokenProtector protector = new KeychainPersonalAccessTokenProtector(commandRunner, new FakeRuntimePlatform(isMacOS: true));

        string firstReference = protector.Protect("pat-1");
        string secondReference = protector.Protect("pat-2", firstReference);

        Assert.Equal(firstReference, secondReference);
        Assert.StartsWith("secure-store:keychain:", firstReference, StringComparison.Ordinal);
        Assert.Equal("pat-2", protector.Unprotect(secondReference));
    }

    /// <summary>
    /// Verifies that a Secret Service protector stores the token via standard input.
    /// </summary>
    [Fact]
    public void SecretServiceProtector_StoresSecretUsingStandardInput()
    {
        FakeCommandRunner commandRunner = new FakeCommandRunner();
        commandRunner.SetAvailability("secret-tool", true);
        commandRunner.OnRun = (command, arguments, standardInput) =>
        {
            Assert.Equal("secret-tool", command);
            if (arguments[0] == "store")
            {
                Assert.Equal("--label=ArchHarness Personal Access Token", arguments[1]);
                Assert.Equal("service", arguments[2]);
                Assert.Equal("ArchHarness", arguments[3]);
                Assert.Equal("account", arguments[4]);
                string secretId = arguments[5];
                commandRunner.StoredSecrets[secretId] = standardInput ?? string.Empty;
                return new LocalCommandResult(0, string.Empty, string.Empty);
            }

            if (arguments[0] == "lookup")
            {
                string secretId = arguments[4];
                return new LocalCommandResult(0, commandRunner.StoredSecrets[secretId] + Environment.NewLine, string.Empty);
            }

            throw new InvalidOperationException("Unexpected command.");
        };

        SecretServicePersonalAccessTokenProtector protector = new SecretServicePersonalAccessTokenProtector(commandRunner, new FakeRuntimePlatform(isLinux: true));

        string reference = protector.Protect("pat-linux");

        Assert.StartsWith("secure-store:secret-service:", reference, StringComparison.Ordinal);
        Assert.Equal("pat-linux", protector.Unprotect(reference));
    }

    private sealed class FakeRuntimePlatform : IRuntimePlatform
    {
        public FakeRuntimePlatform(bool isWindows = false, bool isMacOS = false, bool isLinux = false)
        {
            this.IsWindows = isWindows;
            this.IsMacOS = isMacOS;
            this.IsLinux = isLinux;
        }

        public bool IsWindows { get; }

        public bool IsMacOS { get; }

        public bool IsLinux { get; }
    }

    private sealed class FakeCommandRunner : ILocalCommandRunner
    {
        private readonly Dictionary<string, bool> _availability = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> StoredSecrets { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        public Func<string, IReadOnlyList<string>, string?, LocalCommandResult>? OnRun { get; set; }

        public string? LastSecretId { get; set; }

        public bool IsCommandAvailable(string commandName)
            => this._availability.TryGetValue(commandName, out bool available) && available;

        public LocalCommandResult Run(string commandName, IReadOnlyList<string> arguments, string? standardInput = null)
            => this.OnRun?.Invoke(commandName, arguments, standardInput)
                ?? new LocalCommandResult(0, string.Empty, string.Empty);

        public void SetAvailability(string commandName, bool available)
        {
            this._availability[commandName] = available;
        }
    }
}