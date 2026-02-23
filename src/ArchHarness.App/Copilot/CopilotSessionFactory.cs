using System.Collections.Concurrent;
using System.Text;
using ArchHarness.App.Core;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Copilot;

public interface ICopilotSessionFactory
{
    ICopilotSession Create(string model);
}

public sealed class CopilotSessionFactory : ICopilotSessionFactory, IAsyncDisposable
{
    private readonly CopilotOptions _options;
    private readonly ICopilotGovernancePolicy _governance;
    private readonly ICopilotUserInputBridge _userInputBridge;
    private readonly IUserInputState _userInputState;
    private readonly ICopilotSessionEventStream _eventStream;
    private readonly Task<GitHub.Copilot.SDK.CopilotClient> _clientTask;
    private readonly ConcurrentDictionary<string, Lazy<Task<SessionHandle>>> _sessionHandles = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _sessionInactivityTimeoutSeconds;
    private readonly int _sessionAbsoluteTimeoutSeconds;

    public CopilotSessionFactory(
        IOptions<CopilotOptions> options,
        ICopilotGovernancePolicy governance,
        ICopilotUserInputBridge userInputBridge,
        IUserInputState userInputState,
        ICopilotSessionEventStream eventStream)
    {
        _options = options.Value;
        _governance = governance;
        _userInputBridge = userInputBridge;
        _userInputState = userInputState;
        _eventStream = eventStream;
        _sessionInactivityTimeoutSeconds = Math.Max(0, options.Value.SessionResponseTimeoutSeconds);
        _sessionAbsoluteTimeoutSeconds = Math.Max(0, options.Value.SessionAbsoluteTimeoutSeconds);
        _clientTask = InitializeClientAsync(options.Value);
    }

    public ICopilotSession Create(string model)
        => new SdkCopilotSession(model, this, _userInputState, _eventStream, _sessionInactivityTimeoutSeconds, _sessionAbsoluteTimeoutSeconds);

    public async ValueTask DisposeAsync()
    {
        if (!_clientTask.IsCompletedSuccessfully)
        {
            return;
        }

        foreach (var lazyHandle in _sessionHandles.Values)
        {
            if (lazyHandle.IsValueCreated)
            {
                var handle = await lazyHandle.Value;
                await handle.Session.DisposeAsync();
                handle.Gate.Dispose();
            }
        }

        await _clientTask.Result.DisposeAsync();
    }

    private static async Task<GitHub.Copilot.SDK.CopilotClient> InitializeClientAsync(CopilotOptions options)
    {
        var clientOptions = CopilotClientOptionsFactory.Build(options, autoRestart: true);

        var client = new GitHub.Copilot.SDK.CopilotClient(clientOptions);
        await client.StartAsync();
        return client;
    }

    private Task<SessionHandle> GetOrCreateSessionHandleAsync(string model)
    {
        var lazy = _sessionHandles.GetOrAdd(
            model,
            m => new Lazy<Task<SessionHandle>>(() => CreateSessionHandleAsync(m), LazyThreadSafetyMode.ExecutionAndPublication));
        return lazy.Value;
    }

    private async Task<SessionHandle> CreateSessionHandleAsync(string model)
    {
        var client = await _clientTask;
        var config = new SessionConfig
        {
            Model = model,
            Streaming = _options.StreamingResponses,
            OnUserInputRequest = async (request, _) => await _userInputBridge.RequestInputAsync(request),
            Hooks = new SessionHooks
            {
                OnPreToolUse = async (input, _) => await _governance.OnPreToolUseAsync(input),
                OnPostToolUse = async (input, _) => await _governance.OnPostToolUseAsync(input)
            }
        };

        if (_options.AvailableTools.Count > 0)
        {
            config.AvailableTools = _options.AvailableTools;
        }

        if (_options.ExcludedTools.Count > 0)
        {
            config.ExcludedTools = _options.ExcludedTools;
        }

        var session = await client.CreateSessionAsync(config);
        return new SessionHandle(session, new SemaphoreSlim(1, 1));
    }

    private sealed class SdkCopilotSession(
        string model,
        CopilotSessionFactory factory,
        IUserInputState userInputState,
        ICopilotSessionEventStream eventStream,
        int sessionInactivityTimeoutSeconds,
        int sessionAbsoluteTimeoutSeconds) : ICopilotSession
    {
        public async Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken)
        {
            var handle = await factory.GetOrCreateSessionHandleAsync(model);
            await handle.Gate.WaitAsync(cancellationToken);
            var completion = new StringBuilder();
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            string? finalMessage = null;
            var lastEventType = "none";
            var startedAt = DateTimeOffset.UtcNow;
            var lastEventTicks = startedAt.UtcTicks;

            using var subscription = handle.Session.On(evt =>
            {
                lastEventType = evt.Type;
                Interlocked.Exchange(ref lastEventTicks, DateTimeOffset.UtcNow.UtcTicks);

                var eventType = ResolveEventType(evt);
                if (IsLifecycleEvent(eventType))
                {
                    eventStream.Publish(new CopilotSessionLifecycleEvent(
                        DateTimeOffset.UtcNow,
                        handle.Session.SessionId,
                        model,
                        eventType,
                        ResolveEventDetails(evt)));
                }

                switch (evt)
                {
                    case AssistantMessageDeltaEvent delta when !string.IsNullOrWhiteSpace(delta.Data.DeltaContent):
                        completion.Append(delta.Data.DeltaContent);
                        break;
                    case AssistantMessageEvent msg when !string.IsNullOrWhiteSpace(msg.Data.Content):
                        finalMessage = msg.Data.Content;
                        break;
                    case SessionErrorEvent err:
                        done.TrySetException(new InvalidOperationException($"Copilot SDK session error: {err.Data.Message}"));
                        break;
                    case SessionIdleEvent:
                        done.TrySetResult();
                        break;
                }
            });

            try
            {
                await handle.Session.SendAsync(new MessageOptions { Prompt = prompt, Mode = "immediate" });
                using var registration = cancellationToken.Register(() => done.TrySetCanceled(cancellationToken));

                while (!done.Task.IsCompleted)
                {
                    var now = DateTimeOffset.UtcNow;
                    var lastEventAt = new DateTimeOffset(Interlocked.Read(ref lastEventTicks), TimeSpan.Zero);

                    var inactivityRemaining = sessionInactivityTimeoutSeconds > 0
                        ? TimeSpan.FromSeconds(sessionInactivityTimeoutSeconds) - (now - lastEventAt)
                        : Timeout.InfiniteTimeSpan;
                    var absoluteRemaining = sessionAbsoluteTimeoutSeconds > 0
                        ? TimeSpan.FromSeconds(sessionAbsoluteTimeoutSeconds) - (now - startedAt)
                        : Timeout.InfiniteTimeSpan;

                    if (absoluteRemaining <= TimeSpan.Zero)
                    {
                        await ThrowTimeoutAsync(
                            handle,
                            userInputState,
                            prompt,
                            model,
                            $"absolute timeout {sessionAbsoluteTimeoutSeconds}s",
                            lastEventType,
                            lastEventAt);
                    }

                    if (sessionInactivityTimeoutSeconds > 0 && inactivityRemaining <= TimeSpan.Zero)
                    {
                        await ThrowTimeoutAsync(
                            handle,
                            userInputState,
                            prompt,
                            model,
                            $"inactivity timeout {sessionInactivityTimeoutSeconds}s",
                            lastEventType,
                            lastEventAt);
                    }

                    TimeSpan wait;
                    if (absoluteRemaining == Timeout.InfiniteTimeSpan)
                    {
                        wait = inactivityRemaining;
                    }
                    else
                    {
                        wait = inactivityRemaining < absoluteRemaining ? inactivityRemaining : absoluteRemaining;
                    }

                    var completedTask = await Task.WhenAny(done.Task, Task.Delay(wait, cancellationToken));
                    if (completedTask == done.Task)
                    {
                        break;
                    }
                }

                await done.Task;
                var response = !string.IsNullOrWhiteSpace(finalMessage)
                    ? finalMessage
                    : completion.ToString().Trim();

                return response;
            }
            finally
            {
                handle.Gate.Release();
            }
        }

        private static async Task ThrowTimeoutAsync(
            SessionHandle handle,
            IUserInputState userInputState,
            string prompt,
            string model,
            string timeoutKind,
            string lastEventType,
            DateTimeOffset lastEventAt)
        {
            await handle.Session.AbortAsync();
            var waitingForUser = userInputState.IsAwaitingInput;
            var promptPreview = prompt.Length <= 140 ? prompt : prompt[..137] + "...";
            throw new TimeoutException(
                $"Copilot SDK timed out ({timeoutKind}) for model '{model}'. " +
                $"LastEvent='{lastEventType}' at {lastEventAt:HH:mm:ss}. " +
                $"AwaitingUserInput={waitingForUser}. Prompt='{promptPreview}'");
        }

        private static bool IsLifecycleEvent(string eventType)
        {
            var normalized = eventType.ToLowerInvariant();
            return normalized.Contains("session.start", StringComparison.Ordinal)
                || normalized.Contains("sessionstart", StringComparison.Ordinal)
                || normalized.Contains("tool.execution.start", StringComparison.Ordinal)
                || normalized.Contains("toolexecutionstart", StringComparison.Ordinal)
                || normalized.Contains("tool.execution.complete", StringComparison.Ordinal)
                || normalized.Contains("toolexecutioncomplete", StringComparison.Ordinal)
                || normalized.Contains("session.compaction.start", StringComparison.Ordinal)
                || normalized.Contains("sessioncompactionstart", StringComparison.Ordinal)
                || normalized.Contains("session.compaction.complete", StringComparison.Ordinal)
                || normalized.Contains("sessioncompactioncomplete", StringComparison.Ordinal);
        }

        private static string ResolveEventType(SessionEvent evt)
            => evt.GetType().GetProperty("Type")?.GetValue(evt)?.ToString() ?? evt.GetType().Name;

        private static string? ResolveEventDetails(SessionEvent evt)
        {
            return evt switch
            {
                SessionErrorEvent err => err.Data.Message,
                _ => null
            };
        }
    }

    private sealed record SessionHandle(CopilotSession Session, SemaphoreSlim Gate);
}
