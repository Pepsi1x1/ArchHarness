using ArchHarness.Web.Services;

namespace ArchHarness.App.Tests.Web;

public sealed class WebRunEventHubTests
{
    [Fact]
    public async Task ReadEventsAsync_ReturnsBufferedEventsBeforeFutureEvents()
    {
        WebRunEventHub hub = new WebRunEventHub();
        WebRunEvent buffered = new WebRunEvent(DateTimeOffset.Parse("2026-03-21T10:00:00Z"), "run-state", "test", "buffered");
        WebRunEvent future = new WebRunEvent(DateTimeOffset.Parse("2026-03-21T10:00:01Z"), "run-state", "test", "future");

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