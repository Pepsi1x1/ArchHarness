using ArchHarness.App.Copilot;

namespace ArchHarness.App.Tests.Core;

public sealed class MulticastEventStreamTests
{
    [Fact]
    public async Task ReadAllAsync_SlowSubscriber_DropsOldestEventsWhenBufferIsFull()
    {
        TestEventStream stream = new TestEventStream();
        using CancellationTokenSource cts = new CancellationTokenSource();
        await using IAsyncEnumerator<int> enumerator = stream.ReadAllAsync(cts.Token).GetAsyncEnumerator();

        ValueTask<bool> firstRead = enumerator.MoveNextAsync();
        stream.Publish(0);

        Assert.True(await firstRead);
        Assert.Equal(0, enumerator.Current);

        for (int value = 1; value <= 300; value++)
        {
            stream.Publish(value);
        }

        List<int> remaining = new List<int>();
        for (int i = 0; i < 256; i++)
        {
            Assert.True(await enumerator.MoveNextAsync());
            remaining.Add(enumerator.Current);
        }

        Assert.Equal(256, remaining.Count);
        Assert.Equal(45, remaining[0]);
        Assert.Equal(300, remaining[^1]);

        await cts.CancelAsync();
        bool observedCancellation = false;
        try
        {
            await enumerator.MoveNextAsync().AsTask();
        }
        catch (OperationCanceledException)
        {
            observedCancellation = true;
        }

        Assert.True(observedCancellation, "Expected MoveNextAsync to observe cancellation.");
    }

    private sealed class TestEventStream : MulticastEventStream<int>
    {
        public void Publish(int evt)
            => this.PublishCore(evt);

        public IAsyncEnumerable<int> ReadAllAsync(CancellationToken cancellationToken)
            => this.ReadAllAsyncCore(cancellationToken);
    }
}