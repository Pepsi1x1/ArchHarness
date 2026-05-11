namespace ArchHarness.App.Storage;

/// <summary>
/// Represents a persisted named project or workspace tracked by the local host.
/// </summary>
/// <param name="ProjectId">Stable identifier for the project.</param>
/// <param name="DisplayName">Friendly display name shown in the shell.</param>
/// <param name="WorkspacePath">Absolute path to the workspace root.</param>
/// <param name="WorkspaceMode">Workspace mode used when starting runs for this project.</param>
/// <param name="PermissionHandlerMode">Default permission mode for this project.</param>
/// <param name="ArchitectureReviewMode">Whether architecture review mode is enabled for the project.</param>
/// <param name="ArchitectureReviewPrompt">Optional project-specific architecture review prompt.</param>
/// <param name="CreatedAtUtc">UTC timestamp when the project was first tracked.</param>
/// <param name="UpdatedAtUtc">UTC timestamp when the project was last updated.</param>
/// <param name="SourceControlProviderName">Display name of the source control provider connection used by this project.</param>
/// <param name="SourceControlProjectName">Project name within the source control system (used by ADO providers).</param>
/// <param name="SourceControlRepositoryName">Repository name within the source control system.</param>
public sealed record PersistedProjectWorkspace(
    string ProjectId,
    string DisplayName,
    string WorkspacePath,
    string WorkspaceMode,
    string PermissionHandlerMode,
    bool ArchitectureReviewMode,
    string? ArchitectureReviewPrompt,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? SourceControlProviderName = null,
    string? SourceControlProjectName = null,
    string? SourceControlRepositoryName = null);
