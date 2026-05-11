namespace ArchHarness.Web.Services;

/// <summary>
/// Provides structured model metadata for the web host.
/// </summary>
public interface IModelMetadataProvider
{
    /// <summary>
    /// Gets the available models for settings and composer UI.
    /// </summary>
    IReadOnlyList<AvailableModelViewModel> GetAvailableModels();

    /// <summary>
    /// Returns whether the specified model is known to the provider.
    /// </summary>
    bool IsKnownModel(string modelId);
}
