namespace ArchHarness.App.Constants;

/// <summary>
/// Well-known workspace initialization mode identifiers used by setup, adapters, and run request builders.
/// </summary>
public static class WorkspaceModes
{
    /// <summary>Workspace mode for an existing folder without git tracking.</summary>
    public const string EXISTING_FOLDER = "existing-folder";

    /// <summary>Workspace mode for creating a new project from scratch.</summary>
    public const string NEW_PROJECT = "new-project";

    /// <summary>Workspace mode for an existing folder with git tracking.</summary>
    public const string EXISTING_GIT = "existing-git";
}
