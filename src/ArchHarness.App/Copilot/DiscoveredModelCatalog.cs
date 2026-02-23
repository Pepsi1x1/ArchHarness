using System.Collections.Concurrent;

namespace ArchHarness.App.Copilot;

public interface IDiscoveredModelCatalog
{
    IReadOnlyCollection<string> GetModels();
    void ReplaceModels(IEnumerable<string> models);
    bool HasModels { get; }
}

public sealed class DiscoveredModelCatalog : IDiscoveredModelCatalog
{
    private readonly ConcurrentDictionary<string, byte> _models = new(StringComparer.OrdinalIgnoreCase);

    public bool HasModels => !_models.IsEmpty;

    public IReadOnlyCollection<string> GetModels()
        => _models.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();

    public void ReplaceModels(IEnumerable<string> models)
    {
        _models.Clear();
        foreach (var model in models.Where(m => !string.IsNullOrWhiteSpace(m)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _models[model] = 1;
        }
    }
}
