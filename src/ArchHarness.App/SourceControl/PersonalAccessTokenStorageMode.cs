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
    /// The token is stored in plain text.
    /// </summary>
    PlainText
}