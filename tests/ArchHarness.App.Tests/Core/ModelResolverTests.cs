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
            new DiscoveredModel(WellKnownModelNames.GPT_5_MINI, 0.5, "GPT-5 Mini"),
            new DiscoveredModel(WellKnownModelNames.CLAUDE_OPUS_4_6, 3, "Claude Opus 4.6"),
            new DiscoveredModel(WellKnownModelNames.CLAUDE_SONNET_4_6, 1, "Claude Sonnet 4.6"),
            new DiscoveredModel(WellKnownModelNames.GPT_5_4, 1, "GPT-5.4"),
            new DiscoveredModel(WellKnownModelNames.GPT_4_1, 1, "GPT-4.1")
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
            new DiscoveredModel(WellKnownModelNames.GPT_5_MINI, 0.5, "GPT-5 Mini"),
            new DiscoveredModel(WellKnownModelNames.CLAUDE_OPUS_4_6, 3, "Claude Opus 4.6"),
            new DiscoveredModel(WellKnownModelNames.CLAUDE_SONNET_4_6, 1, "Claude Sonnet 4.6"),
            new DiscoveredModel(WellKnownModelNames.GPT_4_1, 1, "GPT-4.1")
        });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => resolver.ValidateConfiguredModelsOrThrow());

        Assert.Contains($"backend-developer={WellKnownModelNames.GPT_5_4}", exception.Message, StringComparison.OrdinalIgnoreCase);
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
            new DiscoveredModel(WellKnownModelNames.GPT_5_MINI, 0.5, "GPT-5 Mini"),
            new DiscoveredModel(WellKnownModelNames.CLAUDE_OPUS_4_6, 3, "Claude Opus 4.6"),
            new DiscoveredModel(WellKnownModelNames.CLAUDE_SONNET_4_6, 1, "Claude Sonnet 4.6"),
            new DiscoveredModel(WellKnownModelNames.GPT_5_4, 1, "GPT-5.4"),
            new DiscoveredModel(WellKnownModelNames.GPT_4_1, 1, "GPT-4.1")
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

    /// <summary>
    /// Verifies expected behavior.
    /// </summary>
    [Fact]
    public void ResolveReasoningEffort_PlanningRole_UsesPersistedPlanningValue()
    {
        ModelResolver resolver = CreateResolver(new[]
        {
            new DiscoveredModel(WellKnownModelNames.GPT_5_MINI, 0.5, "GPT-5 Mini"),
            new DiscoveredModel(WellKnownModelNames.CLAUDE_OPUS_4_6, 3, "Claude Opus 4.6"),
            new DiscoveredModel(WellKnownModelNames.CLAUDE_SONNET_4_6, 1, "Claude Sonnet 4.6"),
            new DiscoveredModel(WellKnownModelNames.GPT_5_4, 1, "GPT-5.4"),
            new DiscoveredModel(WellKnownModelNames.GPT_4_1, 1, "GPT-4.1")
        });

        Assert.Equal("xhigh", resolver.ResolveReasoningEffort("planning"));
        Assert.Null(resolver.ResolveReasoningEffort("backend-developer"));
    }

    private static ModelResolver CreateResolver(IEnumerable<DiscoveredModel> discoveredModels)
    {
        AgentsOptions agentsOptions = new AgentsOptions
        {
            Orchestration = new AgentModelOptions { Model = WellKnownModelNames.CLAUDE_OPUS_4_6 },
            Planning = new AgentModelOptions { Model = WellKnownModelNames.GPT_5_4, ReasoningEffort = "xhigh" },
            FrontendDeveloper = new AgentModelOptions { Model = WellKnownModelNames.CLAUDE_SONNET_4_6 },
            BackendDeveloper = new AgentModelOptions { Model = WellKnownModelNames.GPT_5_4 },
            Build = new AgentModelOptions { Model = WellKnownModelNames.GPT_4_1 },
            CodingStyle = new AgentModelOptions { Model = WellKnownModelNames.GPT_5_4 },
            Security = new AgentModelOptions { Model = WellKnownModelNames.GPT_5_4 },
            Architecture = new AgentModelOptions { Model = WellKnownModelNames.CLAUDE_OPUS_4_6 }
        };

        CopilotOptions copilotOptions = new CopilotOptions
        {
            ConversationModel = WellKnownModelNames.GPT_5_MINI
        };

        DiscoveredModelCatalog catalog = new DiscoveredModelCatalog();
        if (discoveredModels.Any())
        {
            catalog.ReplaceModels(discoveredModels);
        }

        FileSystemGlobalSettingsCatalog settingsCatalog = new FileSystemGlobalSettingsCatalog(
            Path.Combine(Path.GetTempPath(), "ArchHarnessModelResolverTests", Guid.NewGuid().ToString("N"), "settings.json"),
            agentsOptions,
            copilotOptions);

        return new ModelResolver(
            Options.Create(agentsOptions),
            Options.Create(copilotOptions),
            catalog,
            settingsCatalog);
    }
}
