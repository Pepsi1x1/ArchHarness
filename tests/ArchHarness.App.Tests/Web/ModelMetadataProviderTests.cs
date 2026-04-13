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
            new DiscoveredModel(WellKnownModelNames.GPT_5_MINI, 0.25, "GPT-5 Mini"),
            new DiscoveredModel(WellKnownModelNames.GPT_5_4, 1, "GPT-5.4", new[] { "low", "medium", "high", "xhigh" }, "medium"),
            new DiscoveredModel(WellKnownModelNames.CLAUDE_OPUS_4_6, 3, "Claude Opus 4.6")
        });

        FileSystemGlobalSettingsCatalog settingsCatalog = new FileSystemGlobalSettingsCatalog(
            Path.Combine(this._root, "settings.json"),
            new AgentsOptions(),
            new CopilotOptions());
        settingsCatalog.UpdateSettings(new UpdatePersistedGlobalSettings(
            ConversationModel: WellKnownModelNames.GPT_5_MINI,
            OrchestrationModel: WellKnownModelNames.CLAUDE_SONNET_4_6,
            PlanningModel: WellKnownModelNames.GPT_5_4,
            PlanningReasoningEffort: "xhigh",
            FrontendDeveloperModel: WellKnownModelNames.CLAUDE_SONNET_4_6,
            BackendDeveloperModel: WellKnownModelNames.GPT_5_4,
            BuildModel: WellKnownModelNames.GPT_4_1,
            CodingStyleModel: WellKnownModelNames.GPT_5_4,
            SecurityModel: WellKnownModelNames.GPT_5_4,
            ArchitectureModel: WellKnownModelNames.CLAUDE_OPUS_4_6,
            WikiDocModel: WellKnownModelNames.GPT_5_4,
            WikiDocReasoningEffort: "xhigh",
            WikiDocParallelism: 4,
            DefaultPermissionHandlerMode: "approve-all",
            DefaultArchitectureReviewMode: false,
            DefaultArchitectureReviewPrompt: null));

        ModelMetadataProvider provider = new ModelMetadataProvider(discovered, settingsCatalog);

        IReadOnlyList<AvailableModelViewModel> models = provider.GetAvailableModels();

        Assert.Contains(models, model => model.ModelId == WellKnownModelNames.CLAUDE_OPUS_4_6 && model.DisplayName == "Claude Opus 4.6" && model.CostBand == "3x" && model.Discovered);
        Assert.Contains(models, model => model.ModelId == WellKnownModelNames.CLAUDE_SONNET_4_6 && model.CostBand == string.Empty && model.ConfiguredFallback);
        Assert.Contains(models, model => model.ModelId == WellKnownModelNames.GPT_5_MINI && model.DisplayName == "GPT-5 Mini" && model.CostBand == "0.25x");
        Assert.Contains(models, model => model.ModelId == WellKnownModelNames.GPT_5_4
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
