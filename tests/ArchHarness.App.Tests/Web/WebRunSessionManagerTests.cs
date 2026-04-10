using ArchHarness.App.Core;
using ArchHarness.App.Copilot;
using ArchHarness.App.Constants;
using ArchHarness.App.Storage;
using ArchHarness.Web.Services;

namespace ArchHarness.App.Tests.Web;

public sealed class WebRunSessionManagerTests
{
    [Fact]
    public async Task DisposeAsync_WaitsForActiveExecutionTaskToObserveShutdown()
    {
        TrackingExecutionRunner executionRunner = new TrackingExecutionRunner();
        WebRunSessionManager manager = new(
            executionRunner,
            new WebRunEventHub(),
            new WebRunSnapshotStore(),
            new AgentStreamEventStream(),
            new CopilotSessionEventStream());

        await manager.StartRunAsync(CreateRequest(), CancellationToken.None);
        await executionRunner.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Task disposeTask = manager.DisposeAsync().AsTask();

        Assert.False(disposeTask.IsCompleted);
        await executionRunner.ShutdownObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await disposeTask;
    }

    private static RunRequest CreateRequest()
        => new(
            TaskPrompt: "Review the architecture boundary changes",
            WorkspacePath: @"C:\workspace",
            WorkspaceMode: WorkspaceModes.EXISTING_FOLDER,
            Workflow: WorkflowNames.AUTO,
            ProjectName: null,
            ModelOverrides: null,
            BuildCommand: null);

    private sealed class TrackingExecutionRunner : IWebRunExecutionRunner
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> ShutdownObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task ExecuteRunAsync(RunRequest request, CancellationTokenSource runCts, CancellationToken shutdownToken)
        {
            _ = request;
            _ = runCts;
            this.Started.TrySetResult(true);

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, shutdownToken);
            }
            catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
            {
                await Task.Delay(50, CancellationToken.None);
                this.ShutdownObserved.TrySetResult(true);
            }
        }

        public Task ExecuteResumeAsync(PersistedRunState runState, CancellationTokenSource runCts, CancellationToken shutdownToken)
        {
            _ = runState;
            _ = runCts;
            _ = shutdownToken;
            throw new NotSupportedException();
        }
    }
}
