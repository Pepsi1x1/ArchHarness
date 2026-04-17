using System.Runtime.CompilerServices;
using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using ArchHarness.App.Storage;
using ArchHarness.App.Tui;

namespace ArchHarness.App.Tests.Tui;

public sealed class ChatTerminalRunControllerTests
{
    [Fact]
    public async Task ExecuteAsync_WhenLiveMonitorDisabled_SkipsStreamAndConsolePolling()
    {
        RecordingRuntime runtime = new RecordingRuntime();
        RecordingAgentStreamEventStream stream = new RecordingAgentStreamEventStream();
        RecordingConsoleInputReader inputReader = new RecordingConsoleInputReader();
        ChatTerminalRunController controller = new ChatTerminalRunController(
            runtime,
            new StubUserInputState(),
            stream,
            inputReader);

        ChatTerminalRunResult? result = await controller.ExecuteAsync(
            new RunRequest("Generate docs", @"C:\workspace", "existing-folder", "wikidoc", null, null, null),
            enableLiveMonitor: false,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(stream.ReadAllCalled);
        Assert.Equal(0, inputReader.KeyAvailableReadCount);
        Assert.Single(result.RunEvents);
    }

    private sealed class RecordingRuntime : IOrchestratorRuntime
    {
        public Task<RunArtefacts> RunAsync(RunRequest request, IProgress<RuntimeProgressEvent>? progress = null, Action<string, string>? onRunContextEstablished = null, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = onRunContextEstablished;
            _ = cancellationToken;
            progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, "wikidoc", "started"));
            return Task.FromResult(new RunArtefacts("run-1", @"C:\workspace\.agent-harness\runs\run-1"));
        }

        public Task<RunArtefacts> ResumeAsync(PersistedRunState runState, IProgress<RuntimeProgressEvent>? progress = null, Action<string, string>? onRunContextEstablished = null, CancellationToken cancellationToken = default)
        {
            _ = runState;
            _ = progress;
            _ = onRunContextEstablished;
            _ = cancellationToken;
            throw new NotSupportedException();
        }

        public Task<RunArtefacts> RegenerateMegaWikiAsync(PersistedRunState runState, IProgress<RuntimeProgressEvent>? progress = null, Action<string, string>? onRunContextEstablished = null, CancellationToken cancellationToken = default)
        {
            _ = runState;
            _ = progress;
            _ = onRunContextEstablished;
            _ = cancellationToken;
            throw new NotSupportedException();
        }
    }

    private sealed class StubUserInputState : IUserInputState
    {
        public bool IsAwaitingInput => false;

        public string? ActiveQuestion => null;

        public void Clear()
        {
        }

        public void SetAwaiting(string? question)
        {
            _ = question;
        }
    }

    private sealed class RecordingAgentStreamEventStream : IAgentStreamEventStream
    {
        public bool ReadAllCalled { get; private set; }

        public void Publish(AgentStreamDeltaEvent evt)
        {
            _ = evt;
        }

        public async IAsyncEnumerable<AgentStreamDeltaEvent> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            this.ReadAllCalled = true;
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class RecordingConsoleInputReader : IConsoleInputReader
    {
        public int KeyAvailableReadCount { get; private set; }

        public bool IsInputRedirected => true;

        public bool KeyAvailable
        {
            get
            {
                this.KeyAvailableReadCount++;
                return false;
            }
        }

        public bool TryReadKey(out ConsoleKeyInfo keyInfo)
        {
            keyInfo = default;
            return false;
        }
    }
}
