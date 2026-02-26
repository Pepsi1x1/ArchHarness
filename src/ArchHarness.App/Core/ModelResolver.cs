using ArchHarness.App.Copilot;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Core;

public interface IModelResolver
{
    string Resolve(string role, IDictionary<string, string>? overrides);
    void ValidateOrThrow(string model);
    IReadOnlyCollection<string> SupportedModels { get; }
}

public sealed class ModelResolver : IModelResolver
{
    private readonly AgentsOptions _agents;
    private readonly CopilotOptions _copilot;
    private readonly IDiscoveredModelCatalog _catalog;

    public ModelResolver(
        IOptions<AgentsOptions> agentOptions,
        IOptions<CopilotOptions> copilotOptions,
        IDiscoveredModelCatalog catalog)
    {
        _agents = agentOptions.Value;
        _copilot = copilotOptions.Value;
        _catalog = catalog;
    }

    public IReadOnlyCollection<string> SupportedModels
        => _catalog.HasModels ? _catalog.GetModels() : _copilot.SupportedModels;

    public string Resolve(string role, IDictionary<string, string>? overrides)
    {
        if (overrides is not null && overrides.TryGetValue(role, out var overrideModel) && !string.IsNullOrWhiteSpace(overrideModel))
        {
            ValidateOrThrow(overrideModel);
            return overrideModel;
        }

        var model = role.ToLowerInvariant() switch
        {
            "orchestration" => _agents.Orchestration.Model,
            "frontend" => _agents.Frontend.Model,
            "builder" => _agents.Builder.Model,
            "style" => _agents.Style.Model,
            "architecture" => _agents.Architecture.Model,
            "conversation" => _copilot.ConversationModel,
            _ => throw new ArgumentOutOfRangeException(nameof(role), $"Unsupported role: {role}")
        };

        ValidateOrThrow(model);
        return model;
    }

    public void ValidateOrThrow(string model)
    {
        var supported = SupportedModels;
        if (supported.Count == 0)
        {
            throw new InvalidOperationException("No supported models configured.");
        }

        var isSupported = supported.Any(m => string.Equals(m, model, StringComparison.OrdinalIgnoreCase));
        if (!isSupported)
        {
            throw new InvalidOperationException(
                $"Model '{model}' is not supported by the configured Copilot model allow-list. Supported models: {string.Join(", ", supported)}");
        }
    }
}
