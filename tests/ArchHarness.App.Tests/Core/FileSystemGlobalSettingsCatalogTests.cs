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

        Assert.Equal("gpt-5-mini", settings.ConversationModel);
        Assert.Equal("gpt-5.4", settings.PlanningModel);
        Assert.Equal("xhigh", settings.PlanningReasoningEffort);
        Assert.Equal("claude-opus-4.6", settings.ArchitectureModel);
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
            ConversationModel: "gpt-5.4",
            OrchestrationModel: "claude-opus-4.6",
            PlanningModel: "gpt-5.4",
            PlanningReasoningEffort: "high",
            FrontendDeveloperModel: "claude-sonnet-4.6",
            BackendDeveloperModel: "gpt-5.4",
            BuildModel: "gpt-4.1",
            CodingStyleModel: "gpt-5.4",
            SecurityModel: "gpt-5.4",
            ArchitectureModel: "claude-opus-4.6",
            DefaultPermissionHandlerMode: "prompt",
            DefaultArchitectureReviewMode: false,
            DefaultArchitectureReviewPrompt: null));

        FileSystemGlobalSettingsCatalog reloaded = CreateCatalog();
        PersistedGlobalSettings reloadedSettings = reloaded.GetSettings();

        Assert.Equal("gpt-5.4", updated.ConversationModel);
        Assert.Equal("gpt-5.4", reloadedSettings.ConversationModel);
        Assert.Equal("gpt-5.4", reloadedSettings.PlanningModel);
        Assert.Equal("high", reloadedSettings.PlanningReasoningEffort);
        Assert.Equal("prompt", reloadedSettings.DefaultPermissionHandlerMode);
        Assert.False(reloadedSettings.DefaultArchitectureReviewMode);
    }

    private FileSystemGlobalSettingsCatalog CreateCatalog()
    {
        AgentsOptions agentsOptions = new AgentsOptions
        {
            Orchestration = new AgentModelOptions { Model = "claude-opus-4.6" },
            Planning = new AgentModelOptions { Model = "gpt-5.4", ReasoningEffort = "xhigh" },
            FrontendDeveloper = new AgentModelOptions { Model = "claude-sonnet-4.6" },
            BackendDeveloper = new AgentModelOptions { Model = "gpt-5.4" },
            Build = new AgentModelOptions { Model = "gpt-4.1" },
            CodingStyle = new AgentModelOptions { Model = "gpt-5.4" },
            Security = new AgentModelOptions { Model = "gpt-5.4" },
            Architecture = new AgentModelOptions { Model = "claude-opus-4.6", ArchitectureLoopMode = true, ArchitectureLoopPrompt = "Review the architecture" }
        };
        CopilotOptions copilotOptions = new CopilotOptions
        {
            ConversationModel = "gpt-5-mini"
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
