using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using ArchHarness.App.SourceControl;
using ArchHarness.App.Storage;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Tests.Core;

public sealed class ModelResolverTests
{
    /// <summary>
    /// Verifies expected behavior.
    /// </summary>
    [Fact]
    public void ValidateConfiguredModelsOrThrow_AllConfiguredModelsDiscovered_DoesNotThrow()
    {
        ModelResolver resolver = CreateResolver(new[]
        {
            new DiscoveredModel("gpt-5-mini", 0.5, "GPT-5 Mini"),
            new DiscoveredModel("claude-opus-4.6", 3, "Claude Opus 4.6"),
            new DiscoveredModel("claude-sonnet-4.6", 1, "Claude Sonnet 4.6"),
            new DiscoveredModel("gpt-5.4", 1, "GPT-5.4"),
            new DiscoveredModel("gpt-4.1", 1, "GPT-4.1")
        });

        Exception? exception = Record.Exception(() => resolver.ValidateConfiguredModelsOrThrow());

        Assert.Null(exception);
    }

    /// <summary>
    /// Verifies expected behavior.
    /// </summary>
    [Fact]
    public void ValidateConfiguredModelsOrThrow_MissingConfiguredModel_ThrowsWithRoleDetails()
    {
        ModelResolver resolver = CreateResolver(new[]
        {
            new DiscoveredModel("gpt-5-mini", 0.5, "GPT-5 Mini"),
            new DiscoveredModel("claude-opus-4.6", 3, "Claude Opus 4.6"),
            new DiscoveredModel("claude-sonnet-4.6", 1, "Claude Sonnet 4.6"),
            new DiscoveredModel("gpt-4.1", 1, "GPT-4.1")
        });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => resolver.ValidateConfiguredModelsOrThrow());

        Assert.Contains("backend-developer=gpt-5.4", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CliPath: copilot", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies expected behavior.
    /// </summary>
    [Fact]
    public void ValidateConfiguredModelsOrThrow_MissingOverrideModel_ThrowsWithOverrideDetails()
    {
        ModelResolver resolver = CreateResolver(new[]
        {
            new DiscoveredModel("gpt-5-mini", 0.5, "GPT-5 Mini"),
            new DiscoveredModel("claude-opus-4.6", 3, "Claude Opus 4.6"),
            new DiscoveredModel("claude-sonnet-4.6", 1, "Claude Sonnet 4.6"),
            new DiscoveredModel("gpt-5.4", 1, "GPT-5.4"),
            new DiscoveredModel("gpt-4.1", 1, "GPT-4.1")
        });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => resolver.ValidateConfiguredModelsOrThrow(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["conversation"] = "not-a-real-model"
            }));

        Assert.Contains("override:conversation=not-a-real-model", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CliPath: copilot", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies expected behavior.
    /// </summary>
    [Fact]
    public void ValidateConfiguredModelsOrThrow_NoDiscoveredModels_DoesNotThrow()
    {
        ModelResolver resolver = CreateResolver(Array.Empty<DiscoveredModel>());

        Exception? exception = Record.Exception(() => resolver.ValidateConfiguredModelsOrThrow());

        Assert.Null(exception);
    }

    private static ModelResolver CreateResolver(IEnumerable<DiscoveredModel> discoveredModels)
    {
        AgentsOptions agentsOptions = new AgentsOptions
        {
            Orchestration = new AgentModelOptions { Model = "claude-opus-4.6" },
            FrontendDeveloper = new AgentModelOptions { Model = "claude-sonnet-4.6" },
            BackendDeveloper = new AgentModelOptions { Model = "gpt-5.4" },
            Build = new AgentModelOptions { Model = "gpt-4.1" },
            CodingStyle = new AgentModelOptions { Model = "gpt-5.4" },
            Security = new AgentModelOptions { Model = "gpt-5.4" },
            Architecture = new AgentModelOptions { Model = "claude-opus-4.6" }
        };

        CopilotOptions copilotOptions = new CopilotOptions
        {
            ConversationModel = "gpt-5-mini"
        };

        DiscoveredModelCatalog catalog = new DiscoveredModelCatalog();
        if (discoveredModels.Any())
        {
            catalog.ReplaceModels(discoveredModels);
        }

        FileSystemGlobalSettingsCatalog settingsCatalog = new FileSystemGlobalSettingsCatalog(
            Path.Combine(Path.GetTempPath(), "ArchHarnessModelResolverTests", Guid.NewGuid().ToString("N"), "settings.json"),
            agentsOptions,
            copilotOptions,
            new TestPersonalAccessTokenProtector());

        return new ModelResolver(
            Options.Create(agentsOptions),
            Options.Create(copilotOptions),
            catalog,
            settingsCatalog);
    }

    private sealed class TestPersonalAccessTokenProtector : IPersonalAccessTokenProtector
    {
        public bool CanProtect => true;

        public string? UnavailableReason => null;

        public string Protect(string personalAccessToken, string? existingProtectedPersonalAccessToken = null) => personalAccessToken;

        public string Unprotect(string protectedPersonalAccessToken) => protectedPersonalAccessToken;
    }
}
