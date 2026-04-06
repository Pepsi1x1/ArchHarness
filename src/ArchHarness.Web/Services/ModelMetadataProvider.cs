using ArchHarness.App.Copilot;
using ArchHarness.App.Storage;
using System.Globalization;

namespace ArchHarness.Web.Services;

/// <summary>
/// Adapts discovered and configured models into stable UI metadata.
/// </summary>
public sealed class ModelMetadataProvider : IModelMetadataProvider
{
    private readonly IDiscoveredModelCatalog _discoveredModelCatalog;
    private readonly IGlobalSettingsCatalog _settingsCatalog;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelMetadataProvider"/> class.
    /// </summary>
    public ModelMetadataProvider(IDiscoveredModelCatalog discoveredModelCatalog, IGlobalSettingsCatalog settingsCatalog)
    {
        this._discoveredModelCatalog = discoveredModelCatalog;
        this._settingsCatalog = settingsCatalog;
    }

    /// <inheritdoc />
    public IReadOnlyList<AvailableModelViewModel> GetAvailableModels()
    {
        IReadOnlyDictionary<string, DiscoveredModel> discovered = this._discoveredModelCatalog
            .GetModels()
            .ToDictionary(model => model.Id, StringComparer.OrdinalIgnoreCase);
        HashSet<string> configured = this.GetConfiguredModels().ToHashSet(StringComparer.OrdinalIgnoreCase);
        return discovered
            .Keys
            .Union(configured, StringComparer.OrdinalIgnoreCase)
            .OrderBy(model => model, StringComparer.OrdinalIgnoreCase)
            .Select(model => BuildModelViewModel(
                model,
                discovered.TryGetValue(model, out DiscoveredModel? discoveredModel) ? discoveredModel : null,
                discovered.ContainsKey(model),
                configured.Contains(model) && !discovered.ContainsKey(model)))
            .ToList();
    }

    /// <inheritdoc />
    public bool IsKnownModel(string modelId)
        => !string.IsNullOrWhiteSpace(modelId)
            && this.GetAvailableModels().Any(model => string.Equals(model.ModelId, modelId, StringComparison.OrdinalIgnoreCase));

    private IEnumerable<string> GetConfiguredModels()
    {
        PersistedGlobalSettings settings = this._settingsCatalog.GetSettings();
        yield return settings.ConversationModel;
        yield return settings.OrchestrationModel;
        yield return settings.PlanningModel;
        yield return settings.FrontendDeveloperModel;
        yield return settings.BackendDeveloperModel;
        yield return settings.BuildModel;
        yield return settings.CodingStyleModel;
        yield return settings.SecurityModel;
        yield return settings.ArchitectureModel;
    }

    private static AvailableModelViewModel BuildModelViewModel(string modelId, DiscoveredModel? discoveredModel, bool discovered, bool configuredFallback)
    {
        string displayName = !string.IsNullOrWhiteSpace(discoveredModel?.DisplayName)
            ? discoveredModel.DisplayName
            : modelId;

        string costBand = discoveredModel?.BillingMultiplier is double billingMultiplier
            ? FormatCostBand(billingMultiplier)
            : string.Empty;

        return new AvailableModelViewModel(
            modelId,
            displayName,
            costBand,
            discovered,
            configuredFallback,
            discoveredModel?.SupportedReasoningEfforts,
            discoveredModel?.DefaultReasoningEffort);
    }

    private static string FormatCostBand(double multiplier)
        => string.Concat(multiplier.ToString("0.##", CultureInfo.InvariantCulture), "x");
}