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
/// Represents a raw SDK event emitted during a Copilot session.
/// </summary>
/// <param name="TimestampUtc">The UTC timestamp of the event.</param>
/// <param name="SessionId">The session identifier.</param>
/// <param name="Model">The model identifier used.</param>
/// <param name="EventType">The SDK event type key.</param>
/// <param name="EventClass">The concrete SDK event CLR type.</param>
/// <param name="PayloadJson">The serialized SDK event payload when serialization succeeds.</param>
/// <param name="SerializationError">The serialization error message when payload capture fails.</param>
public sealed record CopilotSdkRawEvent(
    DateTimeOffset TimestampUtc,
    string SessionId,
    string Model,
    string EventType,
    string EventClass,
    string? PayloadJson,
    string? SerializationError
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
/// Publishes and consumes raw SDK events emitted during a Copilot session.
/// </summary>
public interface ICopilotSdkEventStream
{
    /// <summary>Publishes a raw SDK event to the stream.</summary>
    /// <param name="evt">The event to publish.</param>
    void Publish(CopilotSdkRawEvent evt);

    /// <summary>Reads all raw SDK events as an async enumerable.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of raw SDK events.</returns>
    IAsyncEnumerable<CopilotSdkRawEvent> ReadAllAsync(CancellationToken cancellationToken);
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

    protected void PublishCore(TEvent evt)
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

    protected async IAsyncEnumerable<TEvent> ReadAllAsyncCoreAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
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
    void ICopilotSessionEventStream.Publish(CopilotSessionLifecycleEvent evt)
        => this.PublishCore(evt);

    /// <inheritdoc />
    IAsyncEnumerable<CopilotSessionLifecycleEvent> ICopilotSessionEventStream.ReadAllAsync(CancellationToken cancellationToken)
        => this.ReadAllAsyncCoreAsync(cancellationToken);
}

/// <summary>
/// Channel-backed implementation of <see cref="ICopilotSdkEventStream"/>.
/// </summary>
public sealed class CopilotSdkEventStream : MulticastEventStream<CopilotSdkRawEvent>, ICopilotSdkEventStream
{
    /// <inheritdoc />
    void ICopilotSdkEventStream.Publish(CopilotSdkRawEvent evt)
        => this.PublishCore(evt);

    /// <inheritdoc />
    IAsyncEnumerable<CopilotSdkRawEvent> ICopilotSdkEventStream.ReadAllAsync(CancellationToken cancellationToken)
        => this.ReadAllAsyncCoreAsync(cancellationToken);
}

/// <summary>
/// Channel-backed implementation of <see cref="IAgentStreamEventStream"/>.
/// </summary>
public sealed class AgentStreamEventStream : MulticastEventStream<AgentStreamDeltaEvent>, IAgentStreamEventStream
{
    /// <inheritdoc />
    void IAgentStreamEventStream.Publish(AgentStreamDeltaEvent evt)
        => this.PublishCore(evt);

    /// <inheritdoc />
    IAsyncEnumerable<AgentStreamDeltaEvent> IAgentStreamEventStream.ReadAllAsync(CancellationToken cancellationToken)
        => this.ReadAllAsyncCoreAsync(cancellationToken);
}
