using System.Runtime.CompilerServices;
using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Tests.Copilot;

public sealed class CopilotClientTests
{
    [Fact]
    public async Task CompleteAsync_SessionNotFound_InvalidatesCachedSessionBeforeRetry()
    {
        ResetAwareSessionFactory sessionFactory = new ResetAwareSessionFactory();
        RecordingSessionEventStream eventStream = new RecordingSessionEventStream();
        CopilotClient client = new CopilotClient(
            sessionFactory,
            new NullClientProvider(),
            new StubModelResolver(),
            eventStream,
            NullLogger<CopilotClient>.Instance,
            Options.Create(new CopilotOptions
            {
                MaxRetries = 1,
                BaseRetryDelayMilliseconds = 0
            }));

        string completion = await client.CompleteAsync("claude-opus-4.6", "Review the architecture.");

        Assert.Equal("Recovered completion", completion);
        Assert.Equal(2, sessionFactory.CreateCount);
        Assert.Equal(1, sessionFactory.InvalidateCount);
        Assert.Contains(eventStream.Events, evt => evt.EventType == "client.completion.session_reset");
    }

    [Fact]
    public async Task CompleteAsync_CallerCancellation_DoesNotRetryAsTransient()
    {
        CancelingSessionFactory sessionFactory = new CancelingSessionFactory();
        RecordingSessionEventStream eventStream = new RecordingSessionEventStream();
        CopilotClient client = new CopilotClient(
            sessionFactory,
            new NullClientProvider(),
            new StubModelResolver(),
            eventStream,
            NullLogger<CopilotClient>.Instance,
            Options.Create(new CopilotOptions
            {
                MaxRetries = 1,
                BaseRetryDelayMilliseconds = 0
            }));

        using CancellationTokenSource cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.CompleteAsync(
            "claude-opus-4.6",
            "Review the architecture.",
            cancellationToken: cancellationSource.Token));

        string completion = await client.CompleteAsync("claude-opus-4.6", "Review the architecture.");

        Assert.Equal("Recovered completion", completion);
        Assert.Equal(2, sessionFactory.CreateCount);
        Assert.Equal(1, sessionFactory.InvalidateCount);
        Assert.DoesNotContain(eventStream.Events, evt => evt.EventType == "client.completion.transient_retry");
    }

    [Fact]
    public async Task CompleteAsync_ConnectionLost_InvalidatesCachedSessionBeforeRetry()
    {
        RecoveringSessionFactory sessionFactory = new RecoveringSessionFactory(new ConnectionLostSession());
        RecordingSessionEventStream eventStream = new RecordingSessionEventStream();
        RecordingClientProvider clientProvider = new RecordingClientProvider();
        CopilotClient client = new CopilotClient(
            sessionFactory,
            clientProvider,
            new StubModelResolver(),
            eventStream,
            NullLogger<CopilotClient>.Instance,
            Options.Create(new CopilotOptions
            {
                MaxRetries = 1,
                BaseRetryDelayMilliseconds = 0
            }));

        string completion = await client.CompleteAsync("gpt-5.4", "Implement the backend change.");

        Assert.Equal("Recovered completion", completion);
        Assert.Equal(2, sessionFactory.CreateCount);
        Assert.Equal(1, sessionFactory.InvalidateCount);
        Assert.Equal(1, clientProvider.InvalidateCount);
        Assert.Contains(eventStream.Events, evt => evt.EventType == "client.completion.session_reset");
        Assert.Contains(eventStream.Events, evt => evt.EventType == "client.completion.client_recycled");
        Assert.Contains(eventStream.Events, evt => evt.EventType == "client.completion.transient_retry");
    }

    [Fact]
    public async Task CompleteAsync_Timeout_InvalidatesCachedSessionBeforeRetry()
    {
        RecoveringSessionFactory sessionFactory = new RecoveringSessionFactory(new TimeoutSession());
        RecordingSessionEventStream eventStream = new RecordingSessionEventStream();
        CopilotClient client = new CopilotClient(
            sessionFactory,
            new NullClientProvider(),
            new StubModelResolver(),
            eventStream,
            NullLogger<CopilotClient>.Instance,
            Options.Create(new CopilotOptions
            {
                MaxRetries = 1,
                BaseRetryDelayMilliseconds = 0
            }));

        string completion = await client.CompleteAsync("gpt-5.4", "Implement the backend change.");

        Assert.Equal("Recovered completion", completion);
        Assert.Equal(2, sessionFactory.CreateCount);
        Assert.Equal(1, sessionFactory.InvalidateCount);
        Assert.Contains(eventStream.Events, evt => evt.EventType == "client.completion.session_reset");
        Assert.Contains(eventStream.Events, evt => evt.EventType == "client.completion.transient_retry");
    }

    private sealed class ResetAwareSessionFactory : ICopilotSessionFactory
    {
        private int _generation;

        public int CreateCount { get; private set; }

        public int InvalidateCount { get; private set; }

        public ICopilotSession Create(string model, CopilotCompletionOptions? options = null, string? agentId = null, string? agentRole = null)
        {
            _ = model;
            _ = options;
            _ = agentId;
            _ = agentRole;
            CreateCount++;
            return _generation == 0
                ? new FailingSession()
                : new SuccessfulSession();
        }

        public void Invalidate(string model, CopilotCompletionOptions? options = null)
        {
            _ = model;
            _ = options;
            InvalidateCount++;
            _generation++;
        }
    }

    private sealed class RecoveringSessionFactory : ICopilotSessionFactory
    {
        private readonly ICopilotSession _failingSession;
        private int _generation;

        public RecoveringSessionFactory(ICopilotSession failingSession)
        {
            this._failingSession = failingSession;
        }

        public int CreateCount { get; private set; }

        public int InvalidateCount { get; private set; }

        public ICopilotSession Create(string model, CopilotCompletionOptions? options = null, string? agentId = null, string? agentRole = null)
        {
            _ = model;
            _ = options;
            _ = agentId;
            _ = agentRole;
            CreateCount++;
            return _generation == 0
                ? this._failingSession
                : new SuccessfulSession();
        }

        public void Invalidate(string model, CopilotCompletionOptions? options = null)
        {
            _ = model;
            _ = options;
            InvalidateCount++;
            _generation++;
        }
    }

    private sealed class FailingSession : ICopilotSession
    {
        public Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken)
        {
            _ = prompt;
            _ = cancellationToken;
            throw new IOException("Communication error with Copilot CLI: Request session.send failed with message: Session not found: archharness-20260407T112139406-1bae590277672dd0");
        }
    }

    private sealed class SuccessfulSession : ICopilotSession
    {
        public Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken)
        {
            _ = prompt;
            _ = cancellationToken;
            return Task.FromResult("Recovered completion");
        }
    }

    private sealed class ConnectionLostSession : ICopilotSession
    {
        public Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken)
        {
            _ = prompt;
            _ = cancellationToken;
            throw new IOException("Communication error with Copilot CLI: The JSON-RPC connection with the remote party was lost before the request could complete.");
        }
    }

    private sealed class TimeoutSession : ICopilotSession
    {
        public Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken)
        {
            _ = prompt;
            _ = cancellationToken;
            throw new TimeoutException("Copilot SDK timed out (absolute timeout 900s) for model 'gpt-5.4'. LastEvent='report_intent' at 18:57:59. AwaitingUserInput=False.");
        }
    }

    private sealed class CancelingSessionFactory : ICopilotSessionFactory
    {
        private int _generation;

        public int CreateCount { get; private set; }

        public int InvalidateCount { get; private set; }

        public ICopilotSession Create(string model, CopilotCompletionOptions? options = null, string? agentId = null, string? agentRole = null)
        {
            _ = model;
            _ = options;
            _ = agentId;
            _ = agentRole;
            CreateCount++;
            return _generation == 0
                ? new CancelingSession()
                : new SuccessfulSession();
        }

        public void Invalidate(string model, CopilotCompletionOptions? options = null)
        {
            _ = model;
            _ = options;
            InvalidateCount++;
            _generation++;
        }
    }

    private sealed class CancelingSession : ICopilotSession
    {
        public Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken)
        {
            _ = prompt;
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private sealed class RecordingSessionEventStream : ICopilotSessionEventStream
    {
        public List<CopilotSessionLifecycleEvent> Events { get; } = new List<CopilotSessionLifecycleEvent>();

        public void Publish(CopilotSessionLifecycleEvent evt)
            => Events.Add(evt);

        public async IAsyncEnumerable<CopilotSessionLifecycleEvent> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class StubModelResolver : IModelResolver
    {
        public IReadOnlyCollection<string> GetSupportedModels()
            => Array.Empty<string>();

        public string Resolve(string role, IDictionary<string, string>? overrides)
        {
            _ = role;
            _ = overrides;
            return "claude-opus-4.6";
        }

        public string? ResolveReasoningEffort(string role)
        {
            _ = role;
            return null;
        }

        public void ValidateConfiguredModelsOrThrow(IDictionary<string, string>? overrides = null)
        {
            _ = overrides;
        }

        public void ValidateOrThrow(string model)
        {
            _ = model;
        }
    }

    private sealed class NullClientProvider : ICopilotClientProvider
    {
        public Task<GitHub.Copilot.SDK.CopilotClient> GetClientAsync()
            => throw new NotSupportedException("Stub: should not be called.");

        public Task InvalidateAsync()
            => Task.CompletedTask;
    }

    private sealed class RecordingClientProvider : ICopilotClientProvider
    {
        public int InvalidateCount { get; private set; }

        public Task<GitHub.Copilot.SDK.CopilotClient> GetClientAsync()
            => throw new NotSupportedException("Stub: should not be called.");

        public Task InvalidateAsync()
        {
            InvalidateCount++;
            return Task.CompletedTask;
        }
    }
}
