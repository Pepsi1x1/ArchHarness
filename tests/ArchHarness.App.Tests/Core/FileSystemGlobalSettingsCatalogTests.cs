using ArchHarness.App.Core;
using ArchHarness.App.Storage;

namespace ArchHarness.App.Tests.Core;

public sealed class FileSystemGlobalSettingsCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ArchHarnessGlobalSettingsTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void GetSettings_WithoutFile_ReturnsDefaultsFromConfiguredOptions()
    {
        FileSystemGlobalSettingsCatalog catalog = CreateCatalog();

        PersistedGlobalSettings settings = catalog.GetSettings();

        Assert.Equal("gpt-5-mini", settings.ConversationModel);
        Assert.Equal("claude-opus-4.6", settings.ArchitectureModel);
        Assert.Equal("approve-all", settings.DefaultPermissionHandlerMode);
        Assert.True(settings.DefaultArchitectureReviewMode);
        Assert.Equal("Review the architecture", settings.DefaultArchitectureReviewPrompt);
    }

    [Fact]
    public void UpdateSettings_PersistsUpdatedValues()
    {
        FileSystemGlobalSettingsCatalog catalog = CreateCatalog();

        PersistedGlobalSettings updated = catalog.UpdateSettings(new UpdatePersistedGlobalSettings(
            "gpt-5.4",
            "claude-opus-4.6",
            "claude-sonnet-4.6",
            "gpt-5.4",
            "gpt-4.1",
            "gpt-5.4",
            "gpt-5.4",
            "claude-opus-4.6",
            "prompt",
            false,
            null));

        FileSystemGlobalSettingsCatalog reloaded = CreateCatalog();
        PersistedGlobalSettings reloadedSettings = reloaded.GetSettings();

        Assert.Equal("gpt-5.4", updated.ConversationModel);
        Assert.Equal("gpt-5.4", reloadedSettings.ConversationModel);
        Assert.Equal("prompt", reloadedSettings.DefaultPermissionHandlerMode);
        Assert.False(reloadedSettings.DefaultArchitectureReviewMode);
    }

    private FileSystemGlobalSettingsCatalog CreateCatalog()
    {
        AgentsOptions agentsOptions = new AgentsOptions
        {
            Orchestration = new AgentModelOptions { Model = "claude-opus-4.6" },
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