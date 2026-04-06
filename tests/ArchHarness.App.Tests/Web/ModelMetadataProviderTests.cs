using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using ArchHarness.App.Storage;
using ArchHarness.Web.Services;

namespace ArchHarness.App.Tests.Web;

public sealed class ModelMetadataProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ArchHarnessModelMetadataTests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Verifies expected behavior.
    /// </summary>
    [Fact]
    public void GetAvailableModels_CombinesDiscoveredAndConfiguredFallbacks()
    {
        DiscoveredModelCatalog discovered = new DiscoveredModelCatalog();
        discovered.ReplaceModels(new[]
        {
            new DiscoveredModel("gpt-5-mini", 0.25, "GPT-5 Mini"),
            new DiscoveredModel("gpt-5.4", 1, "GPT-5.4", new[] { "low", "medium", "high", "xhigh" }, "medium"),
            new DiscoveredModel("claude-opus-4.6", 3, "Claude Opus 4.6")
        });

        FileSystemGlobalSettingsCatalog settingsCatalog = new FileSystemGlobalSettingsCatalog(
            Path.Combine(this._root, "settings.json"),
            new AgentsOptions(),
            new CopilotOptions());
        settingsCatalog.UpdateSettings(new UpdatePersistedGlobalSettings(
            ConversationModel: "gpt-5-mini",
            OrchestrationModel: "claude-sonnet-4.6",
            PlanningModel: "gpt-5.4",
            PlanningReasoningEffort: "xhigh",
            FrontendDeveloperModel: "claude-sonnet-4.6",
            BackendDeveloperModel: "gpt-5.4",
            BuildModel: "gpt-4.1",
            CodingStyleModel: "gpt-5.4",
            SecurityModel: "gpt-5.4",
            ArchitectureModel: "claude-opus-4.6",
            DefaultPermissionHandlerMode: "approve-all",
            DefaultArchitectureReviewMode: false,
            DefaultArchitectureReviewPrompt: null));

        ModelMetadataProvider provider = new ModelMetadataProvider(discovered, settingsCatalog);

        IReadOnlyList<AvailableModelViewModel> models = provider.GetAvailableModels();

        Assert.Contains(models, model => model.ModelId == "claude-opus-4.6" && model.DisplayName == "Claude Opus 4.6" && model.CostBand == "3x" && model.Discovered);
        Assert.Contains(models, model => model.ModelId == "claude-sonnet-4.6" && model.CostBand == string.Empty && model.ConfiguredFallback);
        Assert.Contains(models, model => model.ModelId == "gpt-5-mini" && model.DisplayName == "GPT-5 Mini" && model.CostBand == "0.25x");
        Assert.Contains(models, model => model.ModelId == "gpt-5.4"
            && model.DefaultReasoningEffort == "medium"
            && model.SupportedReasoningEfforts is not null
            && model.SupportedReasoningEfforts.SequenceEqual(new[] { "low", "medium", "high", "xhigh" }));
    }

    public void Dispose()
    {
        if (Directory.Exists(this._root))
        {
            Directory.Delete(this._root, recursive: true);
        }
    }
}
