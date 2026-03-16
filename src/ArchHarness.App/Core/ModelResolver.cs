using ArchHarness.App.Copilot;
using ArchHarness.App.Storage;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Core;

/// <summary>
/// Resolves the model identifier for a given agent role, applying overrides and validating against discovered Copilot models when available.
/// </summary>
public interface IModelResolver
{
    /// <summary>
    /// Resolves the model for the specified role, applying any overrides.
    /// </summary>
    /// <param name="role">The agent role identifier.</param>
    /// <param name="overrides">Optional model overrides keyed by role.</param>
    /// <returns>The resolved model identifier.</returns>
    string Resolve(string role, IDictionary<string, string>? overrides);

    /// <summary>
    /// Validates that the specified model is in the discovered Copilot model list when that list is available.
    /// </summary>
    /// <param name="model">The model identifier to validate.</param>
    void ValidateOrThrow(string model);

    /// <summary>Gets the collection of discovered model identifiers, if available.</summary>
    IReadOnlyCollection<string> SupportedModels { get; }

    /// <summary>
    /// Validates the configured default models and any request overrides against the discovered Copilot model catalog when available.
    /// </summary>
    /// <param name="overrides">Optional per-role overrides to validate alongside configured defaults.</param>
    void ValidateConfiguredModelsOrThrow(IDictionary<string, string>? overrides = null);
}

/// <summary>
/// Default implementation of <see cref="IModelResolver"/> that resolves models from configuration and a discovered catalog.
/// </summary>
public sealed class ModelResolver : IModelResolver
{
    private readonly string _cliPath;
    private readonly IDiscoveredModelCatalog _catalog;
    private readonly IGlobalSettingsCatalog _settingsCatalog;

    /// <summary>
    /// Initializes a new instance of <see cref="ModelResolver"/>.
    /// </summary>
    /// <param name="agentOptions">The agents configuration options.</param>
    /// <param name="copilotOptions">The Copilot configuration options.</param>
    /// <param name="catalog">The discovered model catalog.</param>
    public ModelResolver(
        IOptions<AgentsOptions> agentOptions,
        IOptions<CopilotOptions> copilotOptions,
        IDiscoveredModelCatalog catalog,
        IGlobalSettingsCatalog settingsCatalog)
    {
        this._cliPath = string.IsNullOrWhiteSpace(copilotOptions.Value.CliPath)
            ? "copilot"
            : copilotOptions.Value.CliPath;
        this._catalog = catalog;
        this._settingsCatalog = settingsCatalog;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> SupportedModels
        => this._catalog.HasModels
            ? this._catalog.GetModels().Select(model => model.Id).ToArray()
            : Array.Empty<string>();

    /// <inheritdoc />
    public string Resolve(string role, IDictionary<string, string>? overrides)
    {
        if (overrides is not null && overrides.TryGetValue(role, out string? overrideModel) && !string.IsNullOrWhiteSpace(overrideModel))
        {
            this.ValidateOrThrow(overrideModel);
            return overrideModel;
        }

        PersistedGlobalSettings settings = this._settingsCatalog.GetSettings();

        string model = role.ToLowerInvariant() switch
        {
            "orchestration" => settings.OrchestrationModel,
            "frontend-developer" => settings.FrontendDeveloperModel,
            "backend-developer" => settings.BackendDeveloperModel,
            "build" => settings.BuildModel,
            "coding-style" => settings.CodingStyleModel,
            "security" => settings.SecurityModel,
            "architecture" => settings.ArchitectureModel,
            "conversation" => settings.ConversationModel,
            _ => throw new ArgumentOutOfRangeException(nameof(role), $"Unsupported role: {role}")
        };

        this.ValidateOrThrow(model);
        return model;
    }

    /// <inheritdoc />
    public void ValidateOrThrow(string model)
    {
        IReadOnlyCollection<string> supported = this.SupportedModels;
        if (supported.Count == 0)
        {
            return;
        }

        bool isSupported = supported.Any(m => string.Equals(m, model, StringComparison.OrdinalIgnoreCase));
        if (!isSupported)
        {
            throw new InvalidOperationException(
                $"Model '{model}' is not available in the Copilot-discovered model list. Discovered models: {string.Join(", ", supported)}");
        }
    }

    /// <inheritdoc />
    public void ValidateConfiguredModelsOrThrow(IDictionary<string, string>? overrides = null)
    {
        IReadOnlyCollection<string> supported = this.SupportedModels;
        if (supported.Count == 0)
        {
            return;
        }

        List<string> invalid = new List<string>();
        foreach ((string label, string model) in GetConfiguredModels(overrides))
        {
            if (string.IsNullOrWhiteSpace(model))
            {
                continue;
            }

            bool isSupported = supported.Any(m => string.Equals(m, model, StringComparison.OrdinalIgnoreCase));
            if (!isSupported)
            {
                invalid.Add($"{label}={model}");
            }
        }

        if (invalid.Count > 0)
        {
            throw new InvalidOperationException(
                $"Configured models are not available in the Copilot-discovered model list: {string.Join(", ", invalid)}. Discovered models: {string.Join(", ", supported)}. Copilot CliPath: {this._cliPath}");
        }
    }

    private IEnumerable<(string Label, string Model)> GetConfiguredModels(IDictionary<string, string>? overrides)
    {
        PersistedGlobalSettings settings = this._settingsCatalog.GetSettings();
        foreach (string role in new[] { "conversation", "orchestration", "frontend-developer", "backend-developer", "build", "coding-style", "security", "architecture" })
        {
            yield return (role, role switch
            {
                "conversation" => settings.ConversationModel,
                "orchestration" => settings.OrchestrationModel,
                "frontend-developer" => settings.FrontendDeveloperModel,
                "backend-developer" => settings.BackendDeveloperModel,
                "build" => settings.BuildModel,
                "coding-style" => settings.CodingStyleModel,
                "security" => settings.SecurityModel,
                "architecture" => settings.ArchitectureModel,
                _ => string.Empty
            });
        }

        if (overrides is null)
        {
            yield break;
        }

        foreach (KeyValuePair<string, string> overridePair in overrides)
        {
            yield return ($"override:{overridePair.Key}", overridePair.Value);
        }
    }
}
