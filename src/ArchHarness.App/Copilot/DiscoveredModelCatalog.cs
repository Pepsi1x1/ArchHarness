using System.Collections.Concurrent;

namespace ArchHarness.App.Copilot;

/// <summary>
/// Represents a model discovered from the Copilot SDK catalog.
/// </summary>
/// <param name="Id">The stable model identifier.</param>
/// <param name="DisplayName">The model display name reported by the SDK, when available.</param>
/// <param name="BillingMultiplier">The billing multiplier reported by the SDK, when available.</param>
public sealed record DiscoveredModel(string Id, double? BillingMultiplier, string? DisplayName = null);

/// <summary>
/// Provides discovery and runtime replacement of supported model identifiers.
/// </summary>
public interface IDiscoveredModelCatalog
{
    /// <summary>Gets the current set of discovered models.</summary>
    /// <returns>An ordered collection of discovered models.</returns>
    IReadOnlyCollection<DiscoveredModel> GetModels();

    /// <summary>
    /// Replaces the current model set with the provided collection.
    /// </summary>
    /// <param name="models">The new discovered models.</param>
    void ReplaceModels(IEnumerable<DiscoveredModel> models);

    /// <summary>Gets whether any models have been discovered.</summary>
    bool HasModels { get; }
}

/// <summary>
/// Thread-safe in-memory implementation of <see cref="IDiscoveredModelCatalog"/>.
/// </summary>
public sealed class DiscoveredModelCatalog : IDiscoveredModelCatalog
{
    private readonly ConcurrentDictionary<string, DiscoveredModel> _models = new ConcurrentDictionary<string, DiscoveredModel>(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public bool HasModels => !this._models.IsEmpty;

    /// <inheritdoc />
    public IReadOnlyCollection<DiscoveredModel> GetModels()
        => this._models.Values
            .OrderBy(model => model.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <inheritdoc />
    public void ReplaceModels(IEnumerable<DiscoveredModel> models)
    {
        this._models.Clear();
        foreach (DiscoveredModel model in models
            .Where(model => !string.IsNullOrWhiteSpace(model.Id))
            .GroupBy(model => model.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()))
        {
            this._models[model.Id] = model;
        }
    }
}
