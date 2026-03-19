namespace ArchHarness.App.Storage;

/// <summary>
/// Provides access to the persisted set of named projects or workspaces.
/// </summary>
public interface IProjectWorkspaceCatalog
{
    /// <summary>
    /// Returns the persisted projects, ordered by most recent update.
    /// </summary>
    IReadOnlyList<PersistedProjectWorkspace> GetProjects();

    /// <summary>
    /// Gets a persisted project by its stable identifier.
    /// </summary>
    PersistedProjectWorkspace? GetProject(string projectId);

    /// <summary>
    /// Creates a new project entry.
    /// </summary>
    PersistedProjectWorkspace CreateProject(
        string? displayName,
        string workspacePath,
        string workspaceMode,
        string permissionHandlerMode,
        bool architectureReviewMode,
        string? architectureReviewPrompt);

    /// <summary>
    /// Ensures a project exists for the specified workspace path and updates its mutable state.
    /// </summary>
    PersistedProjectWorkspace EnsureProject(
        string workspacePath,
        string? displayName,
        string workspaceMode,
        string permissionHandlerMode,
        bool architectureReviewMode,
        string? architectureReviewPrompt);

    /// <summary>
    /// Updates the source control configuration for the specified project.
    /// </summary>
    PersistedProjectWorkspace? UpdateProjectSourceControl(
        string projectId,
        string? providerName,
        string? projectName,
        string? repositoryName);
}