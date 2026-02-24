using System.Collections.Concurrent;
using System.Text;
using ArchHarness.App.Core;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Copilot;

public interface ICopilotSessionFactory
{
    ICopilotSession Create(
        string model,
        CopilotCompletionOptions? options = null,
        string? agentId = null,
        string? agentRole = null);
}

public sealed class CopilotSessionFactory : ICopilotSessionFactory, IAsyncDisposable
{
    private readonly CopilotOptions _options;
    private readonly ICopilotGovernancePolicy _governance;
    private readonly ICopilotUserInputBridge _userInputBridge;
    private readonly IUserInputState _userInputState;
    private readonly ICopilotSessionEventStream _eventStream;
    private readonly IAgentStreamEventStream _agentStream;
    private readonly Task<GitHub.Copilot.SDK.CopilotClient> _clientTask;
    private readonly ConcurrentDictionary<SessionCacheKey, Lazy<Task<SessionHandle>>> _sessionHandles = new();
    private readonly int _sessionInactivityTimeoutSeconds;
    private readonly int _sessionAbsoluteTimeoutSeconds;

    public CopilotSessionFactory(
        IOptions<CopilotOptions> options,
        ICopilotGovernancePolicy governance,
        ICopilotUserInputBridge userInputBridge,
        IUserInputState userInputState,
        ICopilotSessionEventStream eventStream,
        IAgentStreamEventStream agentStream)
    {
        _options = options.Value;
        _governance = governance;
        _userInputBridge = userInputBridge;
        _userInputState = userInputState;
        _eventStream = eventStream;
        _agentStream = agentStream;
        _sessionInactivityTimeoutSeconds = Math.Max(0, options.Value.SessionResponseTimeoutSeconds);
        _sessionAbsoluteTimeoutSeconds = Math.Max(0, options.Value.SessionAbsoluteTimeoutSeconds);
        _clientTask = InitializeClientAsync(options.Value);
    }

    public ICopilotSession Create(
        string model,
        CopilotCompletionOptions? options = null,
        string? agentId = null,
        string? agentRole = null)
        => new SdkCopilotSession(
            model,
            options,
            this,
            _userInputState,
            _eventStream,
            _agentStream,
            agentId,
            agentRole,
            _sessionInactivityTimeoutSeconds,
            _sessionAbsoluteTimeoutSeconds);

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

    private Task<SessionHandle> GetOrCreateSessionHandleAsync(string model, CopilotCompletionOptions? options)
    {
        var key = BuildSessionCacheKey(model, options);
        var lazy = _sessionHandles.GetOrAdd(
            key,
            cacheKey => new Lazy<Task<SessionHandle>>(() => CreateSessionHandleAsync(model, options), LazyThreadSafetyMode.ExecutionAndPublication));
        return lazy.Value;
    }

    private async Task<SessionHandle> CreateSessionHandleAsync(string model, CopilotCompletionOptions? requestOptions)
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

        if (!string.IsNullOrWhiteSpace(requestOptions?.SystemMessage))
        {
            config.SystemMessage = new SystemMessageConfig
            {
                Mode = requestOptions.SystemMessageMode == CopilotSystemMessageMode.Replace
                    ? SystemMessageMode.Replace
                    : SystemMessageMode.Append,
                Content = requestOptions.SystemMessage
            };
        }

        var availableTools = requestOptions?.AvailableTools is { Count: > 0 }
            ? requestOptions.AvailableTools
            : _options.AvailableTools;
        if (availableTools.Count > 0)
        {
            config.AvailableTools = availableTools.ToList();
        }

        var excludedTools = MergeExcludedTools(_options.ExcludedTools, requestOptions?.ExcludedTools);
        if (excludedTools.Length > 0)
        {
            config.ExcludedTools = excludedTools.ToList();
        }

        var session = await client.CreateSessionAsync(config);
        return new SessionHandle(session, new SemaphoreSlim(1, 1));
    }

    private static SessionCacheKey BuildSessionCacheKey(string model, CopilotCompletionOptions? options)
    {
        var systemMessage = options?.SystemMessage ?? string.Empty;
        var mode = options?.SystemMessageMode ?? CopilotSystemMessageMode.Append;
        var available = NormalizeToolList(options?.AvailableTools);
        var excluded = NormalizeToolList(options?.ExcludedTools);
        return new SessionCacheKey(model, systemMessage, mode, available, excluded);
    }

    private static string NormalizeToolList(IReadOnlyList<string>? tools)
    {
        if (tools is null || tools.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(",", tools
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase));
    }

    private static string[] MergeExcludedTools(IReadOnlyList<string> global, IReadOnlyList<string>? additional)
    {
        var merged = global
            .Concat(additional ?? Array.Empty<string>())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return merged;
    }

    private sealed record SessionCacheKey(
        string Model,
        string SystemMessage,
        CopilotSystemMessageMode SystemMessageMode,
        string AvailableTools,
        string ExcludedTools);

    private sealed class SdkCopilotSession(
        string model,
        CopilotCompletionOptions? options,
        CopilotSessionFactory factory,
        IUserInputState userInputState,
        ICopilotSessionEventStream eventStream,
        IAgentStreamEventStream agentStream,
        string? agentId,
        string? agentRole,
        int sessionInactivityTimeoutSeconds,
        int sessionAbsoluteTimeoutSeconds) : ICopilotSession
    {
        public async Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken)
        {
            var handle = await factory.GetOrCreateSessionHandleAsync(model, options);
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
                        agentStream.Publish(new AgentStreamDeltaEvent(
                            DateTimeOffset.UtcNow,
                            string.IsNullOrWhiteSpace(agentId) ? "unknown" : agentId,
                            string.IsNullOrWhiteSpace(agentRole) ? "unknown" : agentRole,
                            delta.Data.DeltaContent));
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

                await AwaitSessionCompletionAsync(
                    done,
                    new SessionTimeoutContext(
                        handle,
                        userInputState,
                        prompt,
                        model,
                        startedAt,
                        () => lastEventType,
                        () => Interlocked.Read(ref lastEventTicks),
                        sessionInactivityTimeoutSeconds,
                        sessionAbsoluteTimeoutSeconds),
                    cancellationToken);

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

        private static async Task AwaitSessionCompletionAsync(
            TaskCompletionSource done,
            SessionTimeoutContext context,
            CancellationToken cancellationToken)
        {
            while (!done.Task.IsCompleted)
            {
                var timeoutState = EvaluateTimeoutState(
                    context.StartedAt,
                    context.GetLastEventTicks(),
                    context.InactivityTimeoutSeconds,
                    context.AbsoluteTimeoutSeconds);
                await ThrowIfTimedOutAsync(context, timeoutState);


                var completedTask = await Task.WhenAny(done.Task, Task.Delay(timeoutState.WaitDuration, cancellationToken));
                if (completedTask == done.Task)
                {
                    return;
                }
            }
        }

        private static TimeoutState EvaluateTimeoutState(
            DateTimeOffset startedAt,
            long lastEventTicks,
            int inactivityTimeoutSeconds,
            int absoluteTimeoutSeconds)
        {
            var now = DateTimeOffset.UtcNow;
            var lastEventAt = new DateTimeOffset(lastEventTicks, TimeSpan.Zero);

            var inactivityRemaining = inactivityTimeoutSeconds > 0
                ? TimeSpan.FromSeconds(inactivityTimeoutSeconds) - (now - lastEventAt)
                : Timeout.InfiniteTimeSpan;
            var absoluteRemaining = absoluteTimeoutSeconds > 0
                ? TimeSpan.FromSeconds(absoluteTimeoutSeconds) - (now - startedAt)
                : Timeout.InfiniteTimeSpan;

            TimeSpan waitDuration;
            if (absoluteRemaining == Timeout.InfiniteTimeSpan)
            {
                waitDuration = inactivityRemaining;
            }
            else
            {
                waitDuration = inactivityRemaining < absoluteRemaining
                    ? inactivityRemaining
                    : absoluteRemaining;
            }

            return new TimeoutState(lastEventAt, inactivityRemaining, absoluteRemaining, waitDuration);
        }

        private static async Task ThrowIfTimedOutAsync(
            SessionTimeoutContext context,
            TimeoutState timeoutState)
        {
            if (timeoutState.AbsoluteRemaining <= TimeSpan.Zero)
            {
                await ThrowTimeoutAsync(
                    context.Handle,
                    context.UserInputState,
                    context.Prompt,
                    context.Model,
                    $"absolute timeout {context.AbsoluteTimeoutSeconds}s",
                    context.GetLastEventType(),
                    timeoutState.LastEventAt);
            }

            if (context.InactivityTimeoutSeconds > 0 && timeoutState.InactivityRemaining <= TimeSpan.Zero)
            {
                await ThrowTimeoutAsync(
                    context.Handle,
                    context.UserInputState,
                    context.Prompt,
                    context.Model,
                    $"inactivity timeout {context.InactivityTimeoutSeconds}s",
                    context.GetLastEventType(),
                    timeoutState.LastEventAt);
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

    private sealed record TimeoutState(
        DateTimeOffset LastEventAt,
        TimeSpan InactivityRemaining,
        TimeSpan AbsoluteRemaining,
        TimeSpan WaitDuration);

    private sealed record SessionHandle(CopilotSession Session, SemaphoreSlim Gate);
    private sealed record SessionTimeoutContext(
        SessionHandle Handle,
        IUserInputState UserInputState,
        string Prompt,
        string Model,
        DateTimeOffset StartedAt,
        Func<string> GetLastEventType,
        Func<long> GetLastEventTicks,
        int InactivityTimeoutSeconds,
        int AbsoluteTimeoutSeconds);
}
