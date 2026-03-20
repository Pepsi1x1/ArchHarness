using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using ArchHarness.App.SourceControl;
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
            new DiscoveredModel("claude-opus-4.6", 3, "Claude Opus 4.6")
        });

        FileSystemGlobalSettingsCatalog settingsCatalog = new FileSystemGlobalSettingsCatalog(
            Path.Combine(this._root, "settings.json"),
            new AgentsOptions(),
            new CopilotOptions(),
            new TestPersonalAccessTokenProtector());
        settingsCatalog.UpdateSettings(new UpdatePersistedGlobalSettings(
            "gpt-5-mini",
            "claude-sonnet-4.6",
            "claude-sonnet-4.6",
            "gpt-5.4",
            "gpt-4.1",
            "gpt-5.4",
            "gpt-5.4",
            "claude-opus-4.6",
            "approve-all",
            false,
            null));

        ModelMetadataProvider provider = new ModelMetadataProvider(discovered, settingsCatalog);

        IReadOnlyList<AvailableModelViewModel> models = provider.GetAvailableModels();

        Assert.Contains(models, model => model.ModelId == "claude-opus-4.6" && model.DisplayName == "Claude Opus 4.6" && model.CostBand == "3x" && model.Discovered);
        Assert.Contains(models, model => model.ModelId == "claude-sonnet-4.6" && model.CostBand == string.Empty && model.ConfiguredFallback);
        Assert.Contains(models, model => model.ModelId == "gpt-5-mini" && model.DisplayName == "GPT-5 Mini" && model.CostBand == "0.25x");
    }

    public void Dispose()
    {
        if (Directory.Exists(this._root))
        {
            Directory.Delete(this._root, recursive: true);
        }
    }

    private sealed class TestPersonalAccessTokenProtector : IPersonalAccessTokenProtector
    {
        public bool CanProtect => true;

        public string? UnavailableReason => null;

        public string Protect(string personalAccessToken, string? existingProtectedPersonalAccessToken = null) => personalAccessToken;

        public string Unprotect(string protectedPersonalAccessToken) => protectedPersonalAccessToken;
    }
}
