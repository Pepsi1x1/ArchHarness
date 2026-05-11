namespace ArchHarness.App.Storage;

/// <summary>
/// Provides persisted global settings for model selection and runtime defaults.
/// </summary>
public interface IGlobalSettingsCatalog
{
    /// <summary>
    /// Gets the effective global settings.
    /// </summary>
    PersistedGlobalSettings GetSettings();

    /// <summary>
    /// Persists an updated global settings snapshot.
    /// </summary>
    PersistedGlobalSettings UpdateSettings(UpdatePersistedGlobalSettings update);
}
