using System.Text.Json;
using ArchHarness.App.Core;

namespace ArchHarness.App.Storage;

/// <summary>
/// Persists named projects or workspaces in a user-scoped JSON file.
/// </summary>
public sealed class FileSystemProjectWorkspaceCatalog : IProjectWorkspaceCatalog
{
    private readonly object _sync = new object();
    private readonly string _storageFilePath;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemProjectWorkspaceCatalog"/> class using the default storage path.
    /// </summary>
    public FileSystemProjectWorkspaceCatalog()
        : this(GetDefaultStorageFilePath())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemProjectWorkspaceCatalog"/> class using an explicit storage file path.
    /// </summary>
    public FileSystemProjectWorkspaceCatalog(string storageFilePath)
    {
        this._storageFilePath = FileSystemStorageHelper.NormalizePath(storageFilePath);
    }

    /// <inheritdoc />
    public IReadOnlyList<PersistedProjectWorkspace> GetProjects()
    {
        lock (this._sync)
        {
            return this.LoadProjects()
                .OrderByDescending(project => project.UpdatedAtUtc)
                .ToList();
        }
    }

    /// <inheritdoc />
    public PersistedProjectWorkspace? GetProject(string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return null;
        }

        lock (this._sync)
        {
            return this.LoadProjects().FirstOrDefault(project => string.Equals(project.ProjectId, projectId, StringComparison.Ordinal));
        }
    }

    /// <inheritdoc />
    public PersistedProjectWorkspace CreateProject(
        string? displayName,
        string workspacePath,
        string workspaceMode,
        string permissionHandlerMode,
        bool architectureReviewMode,
        string? architectureReviewPrompt)
    {
        lock (this._sync)
        {
            List<PersistedProjectWorkspace> projects = this.LoadProjects();
            string normalizedWorkspacePath = NormalizeWorkspacePath(workspacePath);
            if (projects.Any(project => string.Equals(project.WorkspacePath, normalizedWorkspacePath, StringComparison.OrdinalIgnoreCase)))
            {
                return this.EnsureProject(normalizedWorkspacePath, displayName, workspaceMode, permissionHandlerMode, architectureReviewMode, architectureReviewPrompt);
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            PersistedProjectWorkspace created = new PersistedProjectWorkspace(
                Guid.NewGuid().ToString("N"),
                ResolveDisplayName(displayName, normalizedWorkspacePath),
                normalizedWorkspacePath,
                workspaceMode,
                PermissionHandlerModes.Normalize(permissionHandlerMode),
                architectureReviewMode,
                string.IsNullOrWhiteSpace(architectureReviewPrompt) ? null : architectureReviewPrompt.Trim(),
                now,
                now);
            projects.Add(created);
            this.SaveProjects(projects);
            return created;
        }
    }

    /// <inheritdoc />
    public PersistedProjectWorkspace EnsureProject(
        string workspacePath,
        string? displayName,
        string workspaceMode,
        string permissionHandlerMode,
        bool architectureReviewMode,
        string? architectureReviewPrompt)
    {
        lock (this._sync)
        {
            List<PersistedProjectWorkspace> projects = this.LoadProjects();
            string normalizedWorkspacePath = NormalizeWorkspacePath(workspacePath);
            PersistedProjectWorkspace? existing = projects.FirstOrDefault(project => string.Equals(project.WorkspacePath, normalizedWorkspacePath, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                PersistedProjectWorkspace created = new PersistedProjectWorkspace(
                    Guid.NewGuid().ToString("N"),
                    ResolveDisplayName(displayName, normalizedWorkspacePath),
                    normalizedWorkspacePath,
                    workspaceMode,
                    PermissionHandlerModes.Normalize(permissionHandlerMode),
                    architectureReviewMode,
                    string.IsNullOrWhiteSpace(architectureReviewPrompt) ? null : architectureReviewPrompt.Trim(),
                    now,
                    now);
                projects.Add(created);
                this.SaveProjects(projects);
                return created;
            }

            PersistedProjectWorkspace updated = existing with
            {
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? existing.DisplayName : displayName.Trim(),
                WorkspaceMode = workspaceMode,
                PermissionHandlerMode = PermissionHandlerModes.Normalize(permissionHandlerMode),
                ArchitectureReviewMode = architectureReviewMode,
                ArchitectureReviewPrompt = string.IsNullOrWhiteSpace(architectureReviewPrompt) ? null : architectureReviewPrompt.Trim(),
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            int existingIndex = projects.FindIndex(project => string.Equals(project.ProjectId, existing.ProjectId, StringComparison.Ordinal));
            projects[existingIndex] = updated;
            this.SaveProjects(projects);
            return updated;
        }
    }

    /// <inheritdoc />
    public PersistedProjectWorkspace? UpdateProjectSourceControl(
        string projectId,
        string? providerName,
        string? projectName,
        string? repositoryName)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return null;
        }

        lock (this._sync)
        {
            List<PersistedProjectWorkspace> projects = this.LoadProjects();
            PersistedProjectWorkspace? existing = projects.FirstOrDefault(project =>
                string.Equals(project.ProjectId, projectId, StringComparison.Ordinal));
            if (existing is null)
            {
                return null;
            }

            PersistedProjectWorkspace updated = existing with
            {
                SourceControlProviderName = string.IsNullOrWhiteSpace(providerName) ? null : providerName.Trim(),
                SourceControlProjectName = string.IsNullOrWhiteSpace(projectName) ? null : projectName.Trim(),
                SourceControlRepositoryName = string.IsNullOrWhiteSpace(repositoryName) ? null : repositoryName.Trim(),
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            int existingIndex = projects.FindIndex(project =>
                string.Equals(project.ProjectId, existing.ProjectId, StringComparison.Ordinal));
            projects[existingIndex] = updated;
            this.SaveProjects(projects);
            return updated;
        }
    }

    private List<PersistedProjectWorkspace> LoadProjects()
    {
        if (!File.Exists(this._storageFilePath))
        {
            return new List<PersistedProjectWorkspace>();
        }

        try
        {
            string json = File.ReadAllText(this._storageFilePath);
            return JsonSerializer.Deserialize<List<PersistedProjectWorkspace>>(json, JsonDefaults.WEB_INDENTED)
                ?? new List<PersistedProjectWorkspace>();
        }
        catch (IOException)
        {
            return new List<PersistedProjectWorkspace>();
        }
        catch (JsonException)
        {
            return new List<PersistedProjectWorkspace>();
        }
    }

    private void SaveProjects(List<PersistedProjectWorkspace> projects)
    {
        FileSystemStorageHelper.WriteJsonFile(
            this._storageFilePath,
            projects.OrderByDescending(project => project.UpdatedAtUtc),
            JsonDefaults.WEB_INDENTED);
    }

    private static string GetDefaultStorageFilePath()
        => FileSystemStorageHelper.GetAppDataFilePath("projects.json");

    private static string NormalizeWorkspacePath(string workspacePath)
        => FileSystemStorageHelper.NormalizePath(workspacePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string ResolveDisplayName(string? displayName, string workspacePath)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return displayName.Trim();
        }

        string name = Path.GetFileName(workspacePath);
        return string.IsNullOrWhiteSpace(name) ? workspacePath : name;
    }
}
