using ArchHarness.App.Core;
using ArchHarness.Web.Services;

namespace ArchHarness.App.Tests.Web;

public sealed class WebRunSnapshotStoreTests
{
    [Fact]
    public void UpdateStatus_DoesNotOverwriteCancelingWithRunning()
    {
        WebRunSnapshotStore store = new();
        using CancellationTokenSource runCts = store.BeginRunSession(new WebRunSessionStart(
            CancellationToken.None,
            RunStatuses.STARTING,
            new DateTimeOffset(2026, 3, 21, 10, 0, 0, TimeSpan.Zero),
            null,
            null,
            "Review architecture changes",
            @"C:\workspace",
            null));

        CancellationTokenSource? returnedCts = store.RequestCancellation();
        store.UpdateStatus(RunStatuses.RUNNING, null, null);

        WebRunSnapshot snapshot = store.GetSnapshot();
        Assert.Same(runCts, returnedCts);
        Assert.True(snapshot.IsRunning);
        Assert.Equal(RunStatuses.CANCELING, snapshot.Status);
        Assert.Null(snapshot.FailureMessage);

        store.ReleaseRun(runCts);
    }

    [Fact]
    public void UpdateStatus_DoesNotReopenCompletedRun()
    {
        WebRunSnapshotStore store = new();
        using CancellationTokenSource runCts = store.BeginRunSession(new WebRunSessionStart(
            CancellationToken.None,
            RunStatuses.STARTING,
            new DateTimeOffset(2026, 3, 21, 10, 0, 0, TimeSpan.Zero),
            null,
            null,
            "Review architecture changes",
            @"C:\workspace",
            null));

        store.CompleteRun(RunStatuses.COMPLETED, new RunArtefacts("run-123", @"C:\runs\run-123"), null);
        WebRunSnapshot completedSnapshot = store.GetSnapshot();

        store.UpdateStatus(RunStatuses.RUNNING, null, null);

        WebRunSnapshot snapshot = store.GetSnapshot();
        Assert.False(snapshot.IsRunning);
        Assert.Equal(RunStatuses.COMPLETED, snapshot.Status);
        Assert.Equal(completedSnapshot.CompletedAtUtc, snapshot.CompletedAtUtc);
        Assert.Equal("run-123", snapshot.RunId);
        Assert.Equal(@"C:\runs\run-123", snapshot.RunDirectory);

        store.ReleaseRun(runCts);
    }
}