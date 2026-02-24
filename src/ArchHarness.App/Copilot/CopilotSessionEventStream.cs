using System.Threading.Channels;
using ArchHarness.App.Core;

namespace ArchHarness.App.Copilot;

public sealed record CopilotSessionLifecycleEvent(
    DateTimeOffset TimestampUtc,
    string SessionId,
    string Model,
    string EventType,
    string? Details
);

public interface ICopilotSessionEventStream
{
    void Publish(CopilotSessionLifecycleEvent evt);
    IAsyncEnumerable<CopilotSessionLifecycleEvent> ReadAllAsync(CancellationToken cancellationToken);
}

public interface IAgentStreamEventStream
{
    void Publish(AgentStreamDeltaEvent evt);
    IAsyncEnumerable<AgentStreamDeltaEvent> ReadAllAsync(CancellationToken cancellationToken);
}

public sealed class CopilotSessionEventStream : ICopilotSessionEventStream
{
    private readonly Channel<CopilotSessionLifecycleEvent> _channel = Channel.CreateUnbounded<CopilotSessionLifecycleEvent>();

    public void Publish(CopilotSessionLifecycleEvent evt)
        => _channel.Writer.TryWrite(evt);

    public IAsyncEnumerable<CopilotSessionLifecycleEvent> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}

public sealed class AgentStreamEventStream : IAgentStreamEventStream
{
    private readonly Channel<AgentStreamDeltaEvent> _channel = Channel.CreateUnbounded<AgentStreamDeltaEvent>();

    public void Publish(AgentStreamDeltaEvent evt)
        => _channel.Writer.TryWrite(evt);

    public IAsyncEnumerable<AgentStreamDeltaEvent> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}
