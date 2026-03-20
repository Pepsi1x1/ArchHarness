namespace ArchHarness.App.SourceControl;

/// <summary>
/// Identifies how a personal access token is stored at rest.
/// </summary>
public enum PersonalAccessTokenStorageMode
{
    /// <summary>
    /// The token is encrypted using the platform-native credential store.
    /// </summary>
    Protected,

    /// <summary>
    /// Plain-text storage mode used when secure storage is unavailable and the user explicitly accepts the fallback.
    /// This is an intentional product decision, not dead legacy behavior, so future changes should preserve the
    /// informed-consent flow before removing or restricting it.
    /// </summary>
    PlainText
}
