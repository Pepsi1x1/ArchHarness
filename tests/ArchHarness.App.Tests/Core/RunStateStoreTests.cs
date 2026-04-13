using System.Text.Json;
using ArchHarness.App.Constants;
using ArchHarness.App.Core;
using ArchHarness.App.Storage;

namespace ArchHarness.App.Tests.Core;

public sealed class RunStateStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ArchHarnessRunStateStoreTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task WriteStateAsync_SucceedsWhileSharedReaderIsOpenAsync()
    {
        string runDirectory = this.CreateRunDirectory();
        RunStateStore store = new();
        PersistedRunState initialState = CreateState(runDirectory, reviewIteration: 0);
        await store.WriteStateAsync(runDirectory, initialState, CancellationToken.None);

        string runStatePath = FileSystemStorageHelper.GetRunFilePath(runDirectory, "run-state.json");
        using FileStream reader = FileSystemStorageHelper.OpenReadStreamShared(runStatePath);

        await store.WriteStateAsync(runDirectory, CreateState(runDirectory, reviewIteration: 1), CancellationToken.None);

        PersistedRunState updatedState = Assert.IsType<PersistedRunState>(store.GetState(runDirectory));
        Assert.Equal(1, updatedState.ReviewIteration);
    }

    [Fact]
    public async Task UpdateStateAsync_SerializesConcurrentMutationsAsync()
    {
        string runDirectory = this.CreateRunDirectory();
        RunStateStore store = new();
        await store.WriteStateAsync(runDirectory, CreateState(runDirectory, reviewIteration: 0), CancellationToken.None);

        Task<bool> firstUpdate = store.UpdateStateAsync(
            runDirectory,
            current => current is null ? null : current with { ReviewIteration = current.ReviewIteration + 1 },
            CancellationToken.None);
        Task<bool> secondUpdate = store.UpdateStateAsync(
            runDirectory,
            current => current is null ? null : current with { ReviewIteration = current.ReviewIteration + 1 },
            CancellationToken.None);

        bool[] results = await Task.WhenAll(firstUpdate, secondUpdate);

        Assert.All(results, Assert.True);
        PersistedRunState updatedState = Assert.IsType<PersistedRunState>(store.GetState(runDirectory));
        Assert.Equal(2, updatedState.ReviewIteration);
    }

    public void Dispose()
    {
        if (Directory.Exists(this._root))
        {
            Directory.Delete(this._root, recursive: true);
        }
    }

    private string CreateRunDirectory()
    {
        string runDirectory = Path.Combine(this._root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runDirectory);
        return runDirectory;
    }

    private static PersistedRunState CreateState(string runDirectory, int reviewIteration)
        => new(
            RunId: Guid.NewGuid().ToString("N"),
            RunDirectory: runDirectory,
            WorkspaceRoot: runDirectory,
            Status: RunStatuses.RUNNING,
            Phase: RunPhases.EXECUTING_PLAN,
            StartedAtUtc: DateTimeOffset.UtcNow,
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            Request: new RunRequest(
                TaskPrompt: "test",
                WorkspacePath: runDirectory,
                WorkspaceMode: WorkspaceModes.EXISTING_GIT,
                Workflow: WorkflowNames.AUTO,
                ProjectName: null,
                ModelOverrides: null,
                BuildCommand: null),
            CompletedStepIds: Array.Empty<int>(),
            ReviewIteration: reviewIteration,
            FrontendPlan: string.Empty,
            FilesTouched: Array.Empty<string>(),
            Review: new ArchitectureReview(Array.Empty<ArchitectureFinding>(), Array.Empty<string>()),
            SecurityReview: new SecurityReview(Array.Empty<SecurityFinding>(), Array.Empty<string>()),
            FailureMessage: null);
}
