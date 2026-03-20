using ArchHarness.App.Storage;

namespace ArchHarness.App.Tests.Core;

public sealed class FileSystemProjectWorkspaceCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ArchHarnessProjectCatalogTests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Verifies expected behavior.
    /// </summary>
    [Fact]
    public void EnsureProject_PersistsAndReusesWorkspaceEntry()
    {
        string storageFilePath = Path.Combine(this._root, "projects.json");
        FileSystemProjectWorkspaceCatalog catalog = new FileSystemProjectWorkspaceCatalog(storageFilePath);

        PersistedProjectWorkspace created = catalog.EnsureProject(
            "/tmp/workspace-one",
            "Workspace One",
            "existing-folder",
            "approve-all",
            architectureReviewMode: false,
            architectureReviewPrompt: null);

        PersistedProjectWorkspace updated = catalog.EnsureProject(
            "/tmp/workspace-one",
            "Workspace One Renamed",
            "existing-folder",
            "prompt",
            architectureReviewMode: true,
            architectureReviewPrompt: "Review architecture only");

        IReadOnlyList<PersistedProjectWorkspace> projects = catalog.GetProjects();

        Assert.Single(projects);
        Assert.Equal(created.ProjectId, updated.ProjectId);
        Assert.Equal("Workspace One Renamed", projects[0].DisplayName);
        Assert.Equal("prompt", projects[0].PermissionHandlerMode);
        Assert.True(projects[0].ArchitectureReviewMode);
        Assert.Equal("Review architecture only", projects[0].ArchitectureReviewPrompt);
    }

    /// <summary>
    /// Verifies expected behavior.
    /// </summary>
    [Fact]
    public void EnsureProject_NewProjectModeCanTransitionAfterFirstRun()
    {
        string storageFilePath = Path.Combine(this._root, "projects.json");
        FileSystemProjectWorkspaceCatalog catalog = new FileSystemProjectWorkspaceCatalog(storageFilePath);

        PersistedProjectWorkspace created = catalog.CreateProject(
            "Workspace One",
            "/tmp/workspace-one",
            "new-project",
            "approve-all",
            architectureReviewMode: false,
            architectureReviewPrompt: null);

        PersistedProjectWorkspace transitioned = catalog.EnsureProject(
            "/tmp/workspace-one",
            "Workspace One",
            "existing-folder",
            "approve-all",
            architectureReviewMode: false,
            architectureReviewPrompt: null);

        Assert.Equal(created.ProjectId, transitioned.ProjectId);
        Assert.Equal("existing-folder", transitioned.WorkspaceMode);
    }

    public void Dispose()
    {
        if (Directory.Exists(this._root))
        {
            Directory.Delete(this._root, recursive: true);
        }
    }
}
