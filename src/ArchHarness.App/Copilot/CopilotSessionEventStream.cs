using System.Threading.Channels;
using ArchHarness.App.Core;

namespace ArchHarness.App.Copilot;

/// <summary>
/// Represents a lifecycle event emitted during a Copilot session.
/// </summary>
/// <param name="TimestampUtc">The UTC timestamp of the event.</param>
/// <param name="SessionId">The session identifier.</param>
/// <param name="Model">The model identifier used.</param>
/// <param name="EventType">The event type key.</param>
/// <param name="Details">Optional additional details.</param>
public sealed record CopilotSessionLifecycleEvent(
    DateTimeOffset TimestampUtc,
    string SessionId,
    string Model,
    string EventType,
    string? Details
);

/// <summary>
/// Publishes and consumes Copilot session lifecycle events.
/// </summary>
public interface ICopilotSessionEventStream
{
    /// <summary>Publishes a lifecycle event to the stream.</summary>
    /// <param name="evt">The event to publish.</param>
    void Publish(CopilotSessionLifecycleEvent evt);

    /// <summary>Reads all lifecycle events as an async enumerable.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of lifecycle events.</returns>
    IAsyncEnumerable<CopilotSessionLifecycleEvent> ReadAllAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Publishes and consumes agent stream delta events.
/// </summary>
public interface IAgentStreamEventStream
{
    /// <summary>Publishes a delta event to the stream.</summary>
    /// <param name="evt">The event to publish.</param>
    void Publish(AgentStreamDeltaEvent evt);

    /// <summary>Reads all delta events as an async enumerable.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of delta events.</returns>
    IAsyncEnumerable<AgentStreamDeltaEvent> ReadAllAsync(CancellationToken cancellationToken);
}

public abstract class MulticastEventStream<TEvent>
{
    private const int MAX_BUFFERED_EVENTS = 256;

    private readonly object _sync = new object();
    private readonly Dictionary<Guid, Channel<TEvent>> _subscribers = new Dictionary<Guid, Channel<TEvent>>();

    public void Publish(TEvent evt)
    {
        Channel<TEvent>[] subscribers;
        lock (this._sync)
        {
            subscribers = this._subscribers.Values.ToArray();
        }

        foreach (Channel<TEvent> subscriber in subscribers)
        {
            subscriber.Writer.TryWrite(evt);
        }
    }

    public async IAsyncEnumerable<TEvent> ReadAllAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Guid subscriberId = Guid.NewGuid();
        Channel<TEvent> channel = Channel.CreateBounded<TEvent>(new BoundedChannelOptions(MAX_BUFFERED_EVENTS)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        lock (this._sync)
        {
            this._subscribers[subscriberId] = channel;
        }

        try
        {
            await foreach (TEvent evt in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return evt;
            }
        }
        finally
        {
            lock (this._sync)
            {
                this._subscribers.Remove(subscriberId);
            }

            channel.Writer.TryComplete();
        }
    }
}

/// <summary>
/// Channel-backed implementation of <see cref="ICopilotSessionEventStream"/>.
/// </summary>
public sealed class CopilotSessionEventStream : MulticastEventStream<CopilotSessionLifecycleEvent>, ICopilotSessionEventStream
{
    /// <inheritdoc />
    public new void Publish(CopilotSessionLifecycleEvent evt)
        => base.Publish(evt);

    /// <inheritdoc />
    public new IAsyncEnumerable<CopilotSessionLifecycleEvent> ReadAllAsync(CancellationToken cancellationToken)
        => base.ReadAllAsync(cancellationToken);
}

/// <summary>
/// Channel-backed implementation of <see cref="IAgentStreamEventStream"/>.
/// </summary>
public sealed class AgentStreamEventStream : MulticastEventStream<AgentStreamDeltaEvent>, IAgentStreamEventStream
{
    /// <inheritdoc />
    public new void Publish(AgentStreamDeltaEvent evt)
        => base.Publish(evt);

    /// <inheritdoc />
    public new IAsyncEnumerable<AgentStreamDeltaEvent> ReadAllAsync(CancellationToken cancellationToken)
        => base.ReadAllAsync(cancellationToken);
}
