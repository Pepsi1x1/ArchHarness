namespace ArchHarness.Web.Services;

/// <summary>
/// Represents a request to create or register a named project for the shell sidebar.
/// </summary>
public sealed record CreateProjectRequest(
    string? DisplayName,
    string WorkspacePath,
    string WorkspaceMode,
    string PermissionHandlerMode,
    bool ArchitectureReviewMode,
    string? ArchitectureReviewPrompt);