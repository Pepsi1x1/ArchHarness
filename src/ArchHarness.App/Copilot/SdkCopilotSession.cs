using System.Text;
using ArchHarness.App.Core;
using GitHub.Copilot.SDK;

namespace ArchHarness.App.Copilot;

internal sealed record SessionIdentity(string? AgentId, string? AgentRole);

internal sealed record SessionTimeoutSettings(int InactivityTimeoutSeconds, int AbsoluteTimeoutSeconds);

/// <summary>
/// SDK-backed implementation of <see cref="ICopilotSession"/> that handles event streaming,
/// timeout evaluation, and completion orchestration for a single session.
/// </summary>
internal sealed class SdkCopilotSession(
    string model,
    CopilotCompletionOptions? options,
    CopilotSessionFactory factory,
    CopilotSessionFactory.CopilotSessionContext sessionContext,
    SessionIdentity sessionIdentity,
    SessionTimeoutSettings timeoutSettings) : ICopilotSession
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
                sessionContext.SessionEventStream.Publish(new CopilotSessionLifecycleEvent(
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
                    sessionContext.AgentStream.Publish(new AgentStreamDeltaEvent(
                        DateTimeOffset.UtcNow,
                        string.IsNullOrWhiteSpace(sessionIdentity.AgentId) ? "unknown" : sessionIdentity.AgentId,
                        string.IsNullOrWhiteSpace(sessionIdentity.AgentRole) ? "unknown" : sessionIdentity.AgentRole,
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
                    sessionContext.UserInputState,
                    prompt,
                    model,
                    startedAt,
                    () => lastEventType,
                    () => Interlocked.Read(ref lastEventTicks),
                    timeoutSettings.InactivityTimeoutSeconds,
                    timeoutSettings.AbsoluteTimeoutSeconds),
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
        CopilotSessionFactory.SessionHandle handle,
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

    private sealed record TimeoutState(
        DateTimeOffset LastEventAt,
        TimeSpan InactivityRemaining,
        TimeSpan AbsoluteRemaining,
        TimeSpan WaitDuration);

    private sealed record SessionTimeoutContext(
        CopilotSessionFactory.SessionHandle Handle,
        IUserInputState UserInputState,
        string Prompt,
        string Model,
        DateTimeOffset StartedAt,
        Func<string> GetLastEventType,
        Func<long> GetLastEventTicks,
        int InactivityTimeoutSeconds,
        int AbsoluteTimeoutSeconds);
}
