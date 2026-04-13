using ArchHarness.App.Core;
using ArchHarness.App.Storage;

namespace ArchHarness.App.Tests.Core;

public sealed partial class FileSystemGlobalSettingsCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ArchHarnessGlobalSettingsTests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Verifies expected behavior.
    /// </summary>
    [Fact]
    public void GetSettings_WithoutFile_ReturnsDefaultsFromConfiguredOptions()
    {
        FileSystemGlobalSettingsCatalog catalog = CreateCatalog();

        PersistedGlobalSettings settings = catalog.GetSettings();

        Assert.Equal(WellKnownModelNames.GPT_5_MINI, settings.ConversationModel);
        Assert.Equal(WellKnownModelNames.GPT_5_4, settings.PlanningModel);
        Assert.Equal("xhigh", settings.PlanningReasoningEffort);
        Assert.Equal(WellKnownModelNames.CLAUDE_OPUS_4_6, settings.ArchitectureModel);
        Assert.Equal("approve-all", settings.DefaultPermissionHandlerMode);
        Assert.True(settings.DefaultArchitectureReviewMode);
        Assert.Equal("Review the architecture", settings.DefaultArchitectureReviewPrompt);
    }

    /// <summary>
    /// Verifies expected behavior.
    /// </summary>
    [Fact]
    public void UpdateSettings_PersistsUpdatedValues()
    {
        FileSystemGlobalSettingsCatalog catalog = CreateCatalog();

        PersistedGlobalSettings updated = catalog.UpdateSettings(new UpdatePersistedGlobalSettings(
            ConversationModel: WellKnownModelNames.GPT_5_4,
            OrchestrationModel: WellKnownModelNames.CLAUDE_OPUS_4_6,
            PlanningModel: WellKnownModelNames.GPT_5_4,
            PlanningReasoningEffort: "high",
            FrontendDeveloperModel: WellKnownModelNames.CLAUDE_SONNET_4_6,
            BackendDeveloperModel: WellKnownModelNames.GPT_5_4,
            BuildModel: WellKnownModelNames.GPT_4_1,
            CodingStyleModel: WellKnownModelNames.GPT_5_4,
            SecurityModel: WellKnownModelNames.GPT_5_4,
            ArchitectureModel: WellKnownModelNames.CLAUDE_OPUS_4_6,
            WikiDocModel: WellKnownModelNames.GPT_5_4,
            WikiDocReasoningEffort: "xhigh",
            WikiDocParallelism: 4,
            DefaultPermissionHandlerMode: "prompt",
            DefaultArchitectureReviewMode: false,
            DefaultArchitectureReviewPrompt: null));

        FileSystemGlobalSettingsCatalog reloaded = CreateCatalog();
        PersistedGlobalSettings reloadedSettings = reloaded.GetSettings();

        Assert.Equal(WellKnownModelNames.GPT_5_4, updated.ConversationModel);
        Assert.Equal(WellKnownModelNames.GPT_5_4, reloadedSettings.ConversationModel);
        Assert.Equal(WellKnownModelNames.GPT_5_4, reloadedSettings.PlanningModel);
        Assert.Equal("high", reloadedSettings.PlanningReasoningEffort);
        Assert.Equal("prompt", reloadedSettings.DefaultPermissionHandlerMode);
        Assert.False(reloadedSettings.DefaultArchitectureReviewMode);
    }

    private FileSystemGlobalSettingsCatalog CreateCatalog()
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
            Architecture = new AgentModelOptions { Model = WellKnownModelNames.CLAUDE_OPUS_4_6, ArchitectureLoopMode = true, ArchitectureLoopPrompt = "Review the architecture" }
        };
        CopilotOptions copilotOptions = new CopilotOptions
        {
            ConversationModel = WellKnownModelNames.GPT_5_MINI
        };
        return new FileSystemGlobalSettingsCatalog(Path.Combine(this._root, "settings.json"), agentsOptions, copilotOptions);
    }

    public void Dispose()
    {
        if (Directory.Exists(this._root))
        {
            Directory.Delete(this._root, recursive: true);
        }
    }
}
