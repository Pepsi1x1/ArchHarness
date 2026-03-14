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

/// <summary>
/// Channel-backed implementation of <see cref="ICopilotSessionEventStream"/>.
/// </summary>
public sealed class CopilotSessionEventStream : ICopilotSessionEventStream
{
    private readonly Channel<CopilotSessionLifecycleEvent> _channel = Channel.CreateUnbounded<CopilotSessionLifecycleEvent>();

    /// <inheritdoc />
    public void Publish(CopilotSessionLifecycleEvent evt)
        => this._channel.Writer.TryWrite(evt);

    /// <inheritdoc />
    public IAsyncEnumerable<CopilotSessionLifecycleEvent> ReadAllAsync(CancellationToken cancellationToken)
        => this._channel.Reader.ReadAllAsync(cancellationToken);
}

/// <summary>
/// Channel-backed implementation of <see cref="IAgentStreamEventStream"/>.
/// </summary>
public sealed class AgentStreamEventStream : IAgentStreamEventStream
{
    private readonly Channel<AgentStreamDeltaEvent> _channel = Channel.CreateUnbounded<AgentStreamDeltaEvent>();

    /// <inheritdoc />
    public void Publish(AgentStreamDeltaEvent evt)
        => this._channel.Writer.TryWrite(evt);

    /// <inheritdoc />
    public IAsyncEnumerable<AgentStreamDeltaEvent> ReadAllAsync(CancellationToken cancellationToken)
        => this._channel.Reader.ReadAllAsync(cancellationToken);
}
