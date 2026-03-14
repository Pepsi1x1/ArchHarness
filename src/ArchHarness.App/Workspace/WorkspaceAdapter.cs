namespace ArchHarness.App.Workspace;

/// <summary>
/// Abstraction for workspace file operations, supporting initialization, writing, and diffing.
/// </summary>
public interface IWorkspaceAdapter
{
    /// <summary>Gets the root directory path of the workspace.</summary>
    string RootPath { get; }

    /// <summary>
    /// Initializes the workspace, optionally creating a project structure and git repository.
    /// </summary>
    /// <param name="projectName">The optional project name for scaffolding.</param>
    /// <param name="initGit">Whether to initialize a git repository.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task InitializeAsync(string? projectName, bool initGit, CancellationToken cancellationToken);

    /// <summary>
    /// Writes text content to a file at the specified relative path within the workspace.
    /// </summary>
    /// <param name="relativePath">The relative file path within the workspace.</param>
    /// <param name="content">The text content to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task WriteTextAsync(string relativePath, string content, CancellationToken cancellationToken);

    /// <summary>
    /// Produces a diff of changes in the workspace since the last known state.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A string containing the diff output.</returns>
    Task<string> DiffAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Factory for creating <see cref="IWorkspaceAdapter"/> instances based on workspace mode.
/// </summary>
public static class WorkspaceAdapterFactory
{
    /// <summary>
    /// Creates an appropriate workspace adapter for the given mode and root path.
    /// </summary>
    /// <param name="mode">The workspace mode (existing-git, existing-folder, or new-project).</param>
    /// <param name="rootPath">The root directory path of the workspace.</param>
    /// <returns>An <see cref="IWorkspaceAdapter"/> instance for the specified mode.</returns>
    public static IWorkspaceAdapter Create(string mode, string rootPath)
        => mode switch
        {
            "existing-git" => new GitWorkspaceAdapter(rootPath),
            "existing-folder" => new FileSystemWorkspaceAdapter(rootPath),
            "new-project" => new FileSystemWorkspaceAdapter(rootPath),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), $"Unsupported workspace mode: {mode}")
        };
}
