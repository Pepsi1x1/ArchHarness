using System.Collections.Concurrent;

namespace ArchHarness.App.Copilot;

/// <summary>
/// Provides discovery and runtime replacement of supported model identifiers.
/// </summary>
public interface IDiscoveredModelCatalog
{
    /// <summary>Gets the current set of discovered models.</summary>
    /// <returns>An ordered collection of model identifiers.</returns>
    IReadOnlyCollection<string> GetModels();

    /// <summary>
    /// Replaces the current model set with the provided collection.
    /// </summary>
    /// <param name="models">The new model identifiers.</param>
    void ReplaceModels(IEnumerable<string> models);

    /// <summary>Gets whether any models have been discovered.</summary>
    bool HasModels { get; }
}

/// <summary>
/// Thread-safe in-memory implementation of <see cref="IDiscoveredModelCatalog"/>.
/// </summary>
public sealed class DiscoveredModelCatalog : IDiscoveredModelCatalog
{
    private readonly ConcurrentDictionary<string, byte> _models = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public bool HasModels => !this._models.IsEmpty;

    /// <inheritdoc />
    public IReadOnlyCollection<string> GetModels()
        => this._models.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();

    /// <inheritdoc />
    public void ReplaceModels(IEnumerable<string> models)
    {
        this._models.Clear();
        foreach (string model in models.Where(m => !string.IsNullOrWhiteSpace(m)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            this._models[model] = 1;
        }
    }
}
