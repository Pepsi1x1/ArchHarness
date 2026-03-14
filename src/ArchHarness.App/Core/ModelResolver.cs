using ArchHarness.App.Copilot;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Core;

/// <summary>
/// Resolves the model identifier for a given agent role, applying overrides and validating against supported models.
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
    /// Validates that the specified model is in the supported model list, throwing if not.
    /// </summary>
    /// <param name="model">The model identifier to validate.</param>
    void ValidateOrThrow(string model);

    /// <summary>Gets the collection of supported model identifiers.</summary>
    IReadOnlyCollection<string> SupportedModels { get; }
}

/// <summary>
/// Default implementation of <see cref="IModelResolver"/> that resolves models from configuration and a discovered catalog.
/// </summary>
public sealed class ModelResolver : IModelResolver
{
    private readonly AgentsOptions _agents;
    private readonly CopilotOptions _copilot;
    private readonly IDiscoveredModelCatalog _catalog;

    /// <summary>
    /// Initializes a new instance of <see cref="ModelResolver"/>.
    /// </summary>
    /// <param name="agentOptions">The agents configuration options.</param>
    /// <param name="copilotOptions">The Copilot configuration options.</param>
    /// <param name="catalog">The discovered model catalog.</param>
    public ModelResolver(
        IOptions<AgentsOptions> agentOptions,
        IOptions<CopilotOptions> copilotOptions,
        IDiscoveredModelCatalog catalog)
    {
        this._agents = agentOptions.Value;
        this._copilot = copilotOptions.Value;
        this._catalog = catalog;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> SupportedModels
        => this._catalog.HasModels ? this._catalog.GetModels() : this._copilot.SupportedModels;

    /// <inheritdoc />
    public string Resolve(string role, IDictionary<string, string>? overrides)
    {
        if (overrides is not null && overrides.TryGetValue(role, out string? overrideModel) && !string.IsNullOrWhiteSpace(overrideModel))
        {
            this.ValidateOrThrow(overrideModel);
            return overrideModel;
        }

        string model = role.ToLowerInvariant() switch
        {
            "orchestration" => this._agents.Orchestration.Model,
            "frontend-developer" => this._agents.FrontendDeveloper.Model,
            "backend-developer" => this._agents.BackendDeveloper.Model,
            "build" => this._agents.Build.Model,
            "coding-style" => this._agents.CodingStyle.Model,
            "security" => this._agents.Security.Model,
            "architecture" => this._agents.Architecture.Model,
            "conversation" => this._copilot.ConversationModel,
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
            throw new InvalidOperationException("No supported models configured.");
        }

        bool isSupported = supported.Any(m => string.Equals(m, model, StringComparison.OrdinalIgnoreCase));
        if (!isSupported)
        {
            throw new InvalidOperationException(
                $"Model '{model}' is not supported by the configured Copilot model allow-list. Supported models: {string.Join(", ", supported)}");
        }
    }
}
