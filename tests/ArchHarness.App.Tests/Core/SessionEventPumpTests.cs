using System.Threading.Channels;
using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using ArchHarness.App.Storage;

namespace ArchHarness.App.Tests.Core;

public sealed class SessionEventPumpTests
{
    [Fact]
    public async Task PumpSessionEventsAsync_ForwardsEventsToArtefactStore()
    {
        FakeSessionEventStream eventStream = new FakeSessionEventStream();
        FakeArtefactStore artefactStore = new FakeArtefactStore();
        SessionEventPump pump = new SessionEventPump(eventStream, artefactStore);

        CopilotSessionLifecycleEvent evt1 = new CopilotSessionLifecycleEvent(
            DateTimeOffset.UtcNow, "session-1", "gpt-4", "created", "details-1");
        CopilotSessionLifecycleEvent evt2 = new CopilotSessionLifecycleEvent(
            DateTimeOffset.UtcNow, "session-2", "gpt-4", "completed", "details-2");

        eventStream.Enqueue(evt1);
        eventStream.Enqueue(evt2);
        eventStream.Complete();

        await pump.PumpSessionEventsAsync("/fake/run", "run-1", CancellationToken.None);

        Assert.Equal(2, artefactStore.AppendedEvents.Count);
    }

    [Fact]
    public async Task PumpSessionEventsAsync_CancellationStopsCleanly()
    {
        FakeSessionEventStream eventStream = new FakeSessionEventStream();
        FakeArtefactStore artefactStore = new FakeArtefactStore();
        SessionEventPump pump = new SessionEventPump(eventStream, artefactStore);

        CopilotSessionLifecycleEvent evt = new CopilotSessionLifecycleEvent(
            DateTimeOffset.UtcNow, "session-1", "gpt-4", "created", null);
        eventStream.Enqueue(evt);

        using CancellationTokenSource cts = new CancellationTokenSource();
        Task pumpTask = pump.PumpSessionEventsAsync("/fake/run", "run-1", cts.Token);

        // Allow the pump to process the first event, then cancel.
        await Task.Delay(100);
        await cts.CancelAsync();

        // Pump should complete without throwing.
        await pumpTask;

        Assert.True(artefactStore.AppendedEvents.Count >= 1);
    }

    [Fact]
    public async Task PumpSessionEventsAsync_EmptyStream_CompletesWithoutError()
    {
        FakeSessionEventStream eventStream = new FakeSessionEventStream();
        FakeArtefactStore artefactStore = new FakeArtefactStore();
        SessionEventPump pump = new SessionEventPump(eventStream, artefactStore);

        eventStream.Complete();

        await pump.PumpSessionEventsAsync("/fake/run", "run-1", CancellationToken.None);

        Assert.Empty(artefactStore.AppendedEvents);
    }

    private sealed class FakeSessionEventStream : ICopilotSessionEventStream
    {
        private readonly Channel<CopilotSessionLifecycleEvent> _channel = Channel.CreateUnbounded<CopilotSessionLifecycleEvent>();

        public void Enqueue(CopilotSessionLifecycleEvent evt) => _channel.Writer.TryWrite(evt);

        public void Complete() => _channel.Writer.Complete();

        public void Publish(CopilotSessionLifecycleEvent evt) => _channel.Writer.TryWrite(evt);

        public IAsyncEnumerable<CopilotSessionLifecycleEvent> ReadAllAsync(CancellationToken cancellationToken)
            => _channel.Reader.ReadAllAsync(cancellationToken);
    }

    private sealed class FakeArtefactStore : IArtefactStore
    {
        public List<object> AppendedEvents { get; } = new List<object>();

        public Task WriteExecutionPlanAsync(string runDirectory, ExecutionPlan plan, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task WriteArchitectureReviewAsync(string runDirectory, ArchitectureReview review, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task WriteFinalSummaryAsync(string runDirectory, string summary, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task WriteBuildResultAsync(string runDirectory, object payload, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task AppendEventAsync(string runDirectory, object evt, CancellationToken cancellationToken)
        {
            AppendedEvents.Add(evt);
            return Task.CompletedTask;
        }
    }
}
