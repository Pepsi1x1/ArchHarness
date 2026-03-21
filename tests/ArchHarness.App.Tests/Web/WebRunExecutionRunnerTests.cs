using ArchHarness.App.Constants;
using ArchHarness.App.Core;
using ArchHarness.App.Storage;
using ArchHarness.Web.Services;
using System.Collections.Concurrent;

namespace ArchHarness.App.Tests.Web;

public sealed class WebRunExecutionRunnerTests
{
    [Fact]
    public async Task ExecuteRunAsync_RedactsSensitivePromptDetailsInPublishedProgressEvents()
    {
        const string SensitivePrompt = "Use github_pat_abcdefghijklmnopqrstuvwxyz123456 and Bearer abc123secret";

        TestOrchestratorRuntime runtime = new()
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
        TestWebRunEventHub eventHub = new();
        WebRunSnapshotStore snapshotStore = new();
        WebRunExecutionRunner runner = new(runtime, eventHub, snapshotStore);
        using CancellationTokenSource runCts = new();

        await runner.ExecuteRunAsync(CreateRequest(), runCts, CancellationToken.None);

        WebRunEvent progressEvent = await eventHub.RuntimeProgressEventReceived.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.NotNull(progressEvent.Details);
        Assert.DoesNotContain("github_pat_", progressEvent.Details, StringComparison.Ordinal);
        Assert.DoesNotContain("abc123secret", progressEvent.Details, StringComparison.Ordinal);
        Assert.Contains("***REDACTED***", progressEvent.Details, StringComparison.Ordinal);
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

    private sealed class TestOrchestratorRuntime : IOrchestratorRuntime
    {
        public Func<RunRequest, IProgress<RuntimeProgressEvent>?, Action<string, string>?, CancellationToken, Task<RunArtefacts>> RunHandler { get; init; }
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
            => throw new NotSupportedException();
    }

    private sealed class TestWebRunEventHub : IWebRunEventHub
    {
        public ConcurrentQueue<WebRunEvent> Events { get; } = new();

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
}