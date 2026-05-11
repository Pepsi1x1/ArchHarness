using ArchHarness.Web.Services;

namespace ArchHarness.App.Tests.Web;

public sealed class WebRunEventHubTests
{
    [Fact]
    public async Task ReadEventsAsync_ReturnsBufferedEventsBeforeFutureEvents()
    {
        WebRunEventHub hub = new WebRunEventHub();
        WebRunEvent buffered = new WebRunEvent(new DateTimeOffset(2026, 3, 21, 10, 0, 0, TimeSpan.Zero), "run-state", "test", "buffered");
        WebRunEvent future = new WebRunEvent(new DateTimeOffset(2026, 3, 21, 10, 0, 1, TimeSpan.Zero), "run-state", "test", "future");

        hub.Publish(buffered);

        using CancellationTokenSource cts = new CancellationTokenSource();
        await using IAsyncEnumerator<WebRunEvent> enumerator = hub.ReadEventsAsync(cts.Token).GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(buffered, enumerator.Current);

        hub.Publish(future);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(future, enumerator.Current);

        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await enumerator.MoveNextAsync().AsTask());
    }
}
