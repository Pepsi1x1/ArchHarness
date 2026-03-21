using System.Collections.Concurrent;
using System.Threading.Channels;

namespace ArchHarness.Web.Services;

/// <summary>
/// Buffers and broadcasts web run events to active subscribers.
/// </summary>
public interface IWebRunEventHub
{
    /// <summary>
    /// Publishes an event to the buffer and all subscribers.
    /// </summary>
    void Publish(WebRunEvent evt);

    /// <summary>
    /// Clears the buffered event history for a new run.
    /// </summary>
    void Reset();

    /// <summary>
    /// Streams buffered and future events to a subscriber.
    /// </summary>
    IAsyncEnumerable<WebRunEvent> ReadEventsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Completes all subscriber channels.
    /// </summary>
    void CompleteSubscribers();
}

/// <summary>
/// Default implementation of <see cref="IWebRunEventHub"/>.
/// </summary>
public sealed class WebRunEventHub : IWebRunEventHub
{
    private const int MAX_BUFFERED_EVENTS = 256;

    private readonly object _sync = new();
    private readonly List<WebRunEvent> _bufferedEvents = new();
    private readonly ConcurrentDictionary<Guid, Channel<WebRunEvent>> _subscribers = new();

    /// <inheritdoc />
    public void Publish(WebRunEvent evt)
    {
        lock (this._sync)
        {
            this._bufferedEvents.Add(evt);
            if (this._bufferedEvents.Count > MAX_BUFFERED_EVENTS)
            {
                this._bufferedEvents.RemoveAt(0);
            }

            foreach (KeyValuePair<Guid, Channel<WebRunEvent>> subscriber in this._subscribers)
            {
                subscriber.Value.Writer.TryWrite(evt);
            }
        }
    }

    /// <inheritdoc />
    public void Reset()
    {
        lock (this._sync)
        {
            this._bufferedEvents.Clear();
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<WebRunEvent> ReadEventsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Guid subscriberId = Guid.NewGuid();
        Channel<WebRunEvent> channel = Channel.CreateBounded<WebRunEvent>(new BoundedChannelOptions(MAX_BUFFERED_EVENTS)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        lock (this._sync)
        {
            this._subscribers[subscriberId] = channel;

            foreach (WebRunEvent evt in this._bufferedEvents)
            {
                channel.Writer.TryWrite(evt);
            }
        }

        try
        {
            await foreach (WebRunEvent evt in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return evt;
            }
        }
        finally
        {
            this._subscribers.TryRemove(subscriberId, out _);
            channel.Writer.TryComplete();
        }
    }

    /// <inheritdoc />
    public void CompleteSubscribers()
    {
        foreach (Channel<WebRunEvent> channel in this._subscribers.Values)
        {
            channel.Writer.TryComplete();
        }
    }
}
