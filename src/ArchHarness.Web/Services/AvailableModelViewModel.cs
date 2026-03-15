namespace ArchHarness.Web.Services;

/// <summary>
/// Represents a model option returned to the web shell settings UI.
/// </summary>
public sealed record AvailableModelViewModel(
    string ModelId,
    string DisplayName,
    string CostBand,
    bool Discovered,
    bool ConfiguredFallback);