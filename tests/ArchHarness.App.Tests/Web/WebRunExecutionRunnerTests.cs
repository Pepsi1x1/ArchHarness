using System.Collections.Concurrent;
using ArchHarness.App.Constants;
using ArchHarness.App.Core;
using ArchHarness.App.Storage;
using ArchHarness.Web.Services;

namespace ArchHarness.App.Tests.Web;

public sealed class WebRunExecutionRunnerTests
{
    [Fact]
    public async Task ExecuteResumeAsync_ExceptionPersistsFailedRunState()
    {
        PersistedRunState runState = CreatePersistedRunState();
        TestOrchestratorRuntime runtime = new TestOrchestratorRuntime()
        {
            ResumeHandler = (_, _, _, _) => throw new TimeoutException("Delegate stalled waiting for session completion.")
        };
        TestWebRunEventHub eventHub = new TestWebRunEventHub();
        TestRunStateStore runStateStore = new TestRunStateStore();
        runStateStore.SetState(runState.RunDirectory, runState);
        WebRunSnapshotStore snapshotStore = new WebRunSnapshotStore();
        WebRunExecutionRunner runner = new(runtime, eventHub, runStateStore, snapshotStore);
        using CancellationTokenSource runCts = new CancellationTokenSource();

        await runner.ExecuteResumeAsync(runState, runCts, CancellationToken.None);

        PersistedRunState failedState = Assert.IsType<PersistedRunState>(runStateStore.GetState(runState.RunDirectory));
        Assert.Equal(RunStatuses.FAILED, failedState.Status);
        Assert.Equal(RunTerminalPhases.FAILED, failedState.Phase);
        Assert.Equal("Delegate stalled waiting for session completion.", failedState.FailureMessage);
    }

    [Fact]
    public async Task ExecuteResumeAsync_ShutdownCancellationPersistsStoppedRunState()
    {
        PersistedRunState runState = CreatePersistedRunState();
        TestOrchestratorRuntime runtime = new TestOrchestratorRuntime()
        {
            ResumeHandler = async (_, _, _, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new RunArtefacts(runState.RunId, runState.RunDirectory);
            }
        };
        TestWebRunEventHub eventHub = new TestWebRunEventHub();
        TestRunStateStore runStateStore = new TestRunStateStore();
        runStateStore.SetState(runState.RunDirectory, runState);
        WebRunSnapshotStore snapshotStore = new WebRunSnapshotStore();
        WebRunExecutionRunner runner = new(runtime, eventHub, runStateStore, snapshotStore);
        using CancellationTokenSource shutdownCts = new CancellationTokenSource();
        using CancellationTokenSource runCts = CancellationTokenSource.CreateLinkedTokenSource(shutdownCts.Token);

        Task execution = runner.ExecuteResumeAsync(runState, runCts, shutdownCts.Token);
        await shutdownCts.CancelAsync();
        await execution;

        PersistedRunState stoppedState = Assert.IsType<PersistedRunState>(runStateStore.GetState(runState.RunDirectory));
        Assert.Equal(RunStatuses.STOPPED, stoppedState.Status);
        Assert.Equal(RunTerminalPhases.STOPPED, stoppedState.Phase);
        Assert.Equal("Run stopped because the local web host is shutting down.", stoppedState.FailureMessage);
    }

    [Fact]
    public async Task ExecuteRunAsync_RedactsSensitivePromptDetailsInPublishedProgressEvents()
    {
        const string SensitivePrompt = "Use github_pat_abcdefghijklmnopqrstuvwxyz123456 and Bearer abc123secret";

        TestOrchestratorRuntime runtime = new TestOrchestratorRuntime()
        {
            RunHandler = (request, progress, onRunContextEstablished, cancellationToken) =>
            {
                onRunContextEstablished?.Invoke("run-123", @"C:\runs\run-123");
                progress?.Report(new RuntimeProgressEvent(
                    new DateTimeOffset(2026, 3, 21, 10, 0, 0, TimeSpan.Zero),
                    "architecture",
                    "Generating plan",
                    SensitivePrompt));

                return Task.FromResult(new RunArtefacts("run-123", @"C:\runs\run-123"));
            }
        };
        TestWebRunEventHub eventHub = new TestWebRunEventHub();
        TestRunStateStore runStateStore = new TestRunStateStore();
        WebRunSnapshotStore snapshotStore = new WebRunSnapshotStore();
        WebRunExecutionRunner runner = new(runtime, eventHub, runStateStore, snapshotStore);
        using CancellationTokenSource runCts = new CancellationTokenSource();

        await runner.ExecuteRunAsync(CreateRequest(), runCts, CancellationToken.None);

        WebRunEvent progressEvent = await eventHub.RuntimeProgressEventReceived.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.NotNull(progressEvent.Details);
        Assert.DoesNotContain("github_pat_", progressEvent.Details, StringComparison.Ordinal);
        Assert.DoesNotContain("abc123secret", progressEvent.Details, StringComparison.Ordinal);
        Assert.Contains("***REDACTED***", progressEvent.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteRunAsync_PauseRequestPersistsPausedRunState()
    {
        TaskCompletionSource<bool> runStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TestOrchestratorRuntime runtime = new TestOrchestratorRuntime()
        {
            RunHandler = async (request, progress, onRunContextEstablished, cancellationToken) =>
            {
                onRunContextEstablished?.Invoke("run-123", @"C:\runs\run-123");
                runStarted.TrySetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new RunArtefacts("run-123", @"C:\runs\run-123");
            }
        };
        TestWebRunEventHub eventHub = new TestWebRunEventHub();
        TestRunStateStore runStateStore = new TestRunStateStore();
        runStateStore.SetState(@"C:\runs\run-123", new PersistedRunState(
            "run-123",
            @"C:\runs\run-123",
            @"C:\workspace",
            RunStatuses.RUNNING,
            RunPhases.EXECUTING_PLAN,
            new DateTimeOffset(2026, 3, 28, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 28, 10, 1, 0, TimeSpan.Zero),
            CreateRequest(),
            Array.Empty<int>(),
            0,
            string.Empty,
            Array.Empty<string>(),
            new ArchitectureReview(Array.Empty<ArchitectureFinding>(), Array.Empty<string>()),
            new SecurityReview(Array.Empty<SecurityFinding>(), Array.Empty<string>())));
        WebRunSnapshotStore snapshotStore = new WebRunSnapshotStore();
        using CancellationTokenSource runCts = snapshotStore.BeginRunSession(new WebRunSessionStart(
            CancellationToken.None,
            RunStatuses.STARTING,
            new DateTimeOffset(2026, 3, 28, 10, 0, 0, TimeSpan.Zero),
            null,
            null,
            "Review the architecture boundary changes",
            @"C:\workspace",
            null));
        WebRunExecutionRunner runner = new(runtime, eventHub, runStateStore, snapshotStore);

        Task execution = runner.ExecuteRunAsync(CreateRequest(), runCts, CancellationToken.None);
        await runStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        CancellationTokenSource? pauseCts = snapshotStore.RequestPause();
        Assert.Same(runCts, pauseCts);
        await runCts.CancelAsync();
        await execution;

        PersistedRunState pausedState = Assert.IsType<PersistedRunState>(runStateStore.GetState(@"C:\runs\run-123"));
        Assert.Equal(RunStatuses.PAUSED, pausedState.Status);
        Assert.Equal(RunTerminalPhases.PAUSED, pausedState.Phase);

        WebRunSnapshot snapshot = snapshotStore.GetSnapshot();
        Assert.False(snapshot.IsRunning);
        Assert.Equal(RunStatuses.PAUSED, snapshot.Status);
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

    private static PersistedRunState CreatePersistedRunState()
        => new(
            "run-123",
            @"C:\runs\run-123",
            @"C:\workspace",
            RunStatuses.RUNNING,
            RunPhases.EXECUTING_PLAN,
            new DateTimeOffset(2026, 3, 28, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 28, 10, 1, 0, TimeSpan.Zero),
            CreateRequest(),
            Array.Empty<int>(),
            0,
            string.Empty,
            Array.Empty<string>(),
            new ArchitectureReview(Array.Empty<ArchitectureFinding>(), Array.Empty<string>()),
            new SecurityReview(Array.Empty<SecurityFinding>(), Array.Empty<string>()));

    private sealed class TestOrchestratorRuntime : IOrchestratorRuntime
    {
        public Func<RunRequest, IProgress<RuntimeProgressEvent>?, Action<string, string>?, CancellationToken, Task<RunArtefacts>> RunHandler { get; init; }
            = (_, _, _, _) => Task.FromResult(new RunArtefacts("run-default", @"C:\runs\run-default"));

        public Func<PersistedRunState, IProgress<RuntimeProgressEvent>?, Action<string, string>?, CancellationToken, Task<RunArtefacts>> ResumeHandler { get; init; }
            = (_, _, _, _) => Task.FromResult(new RunArtefacts("run-default", @"C:\runs\run-default"));

        public Task<RunArtefacts> RunAsync(
            RunRequest request,
            IProgress<RuntimeProgressEvent>? progress = null,
            Action<string, string>? onRunContextEstablished = null,
            CancellationToken cancellationToken = default)
            => this.RunHandler(request, progress, onRunContextEstablished, cancellationToken);

        public Task<RunArtefacts> ResumeAsync(
            PersistedRunState runState,
            IProgress<RuntimeProgressEvent>? progress = null,
            Action<string, string>? onRunContextEstablished = null,
            CancellationToken cancellationToken = default)
            => this.ResumeHandler(runState, progress, onRunContextEstablished, cancellationToken);

        public Task<RunArtefacts> RegenerateMegaWikiAsync(
            PersistedRunState runState,
            IProgress<RuntimeProgressEvent>? progress = null,
            Action<string, string>? onRunContextEstablished = null,
            CancellationToken cancellationToken = default)
            => this.ResumeHandler(runState, progress, onRunContextEstablished, cancellationToken);
    }

    private sealed class TestWebRunEventHub : IWebRunEventHub
    {
        public ConcurrentQueue<WebRunEvent> Events { get; } = new ConcurrentQueue<WebRunEvent>();

        public TaskCompletionSource<WebRunEvent> RuntimeProgressEventReceived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Publish(WebRunEvent evt)
        {
            this.Events.Enqueue(evt);

            if (evt.Kind == "runtime-progress")
            {
                this.RuntimeProgressEventReceived.TrySetResult(evt);
            }
        }

        public void Reset()
            => this.Events.Clear();

        public IAsyncEnumerable<WebRunEvent> ReadEventsAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public void CompleteSubscribers()
        {
        }
    }

    private sealed class TestRunStateStore : IRunStateStore
    {
        private readonly ConcurrentDictionary<string, PersistedRunState> _states = new(StringComparer.OrdinalIgnoreCase);

        public Task WriteStateAsync(string runDirectory, PersistedRunState state, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            this._states[runDirectory] = state;
            return Task.CompletedTask;
        }

        public PersistedRunState? GetState(string runDirectory)
            => this._states.TryGetValue(runDirectory, out PersistedRunState? state) ? state : null;

        public void SetState(string runDirectory, PersistedRunState state)
            => this._states[runDirectory] = state;
    }
}
