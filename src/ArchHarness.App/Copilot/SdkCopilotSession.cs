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
    /// <inheritdoc />
    public async Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken)
    {
        CopilotSessionFactory.SessionHandle handle = await factory.GetOrCreateSessionHandleAsync(model, options).ConfigureAwait(false);
        await handle.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        StringBuilder completion = new();
        TaskCompletionSource done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        string? finalMessage = null;
        string lastEventType = "none";
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        long lastEventTicks = startedAt.UtcTicks;

        string agentId = string.IsNullOrWhiteSpace(sessionIdentity.AgentId) ? "unknown" : sessionIdentity.AgentId;
        string agentRole = string.IsNullOrWhiteSpace(sessionIdentity.AgentRole) ? "unknown" : sessionIdentity.AgentRole;
        if (!string.IsNullOrWhiteSpace(prompt))
        {
            sessionContext.AgentStream.Publish(new AgentStreamDeltaEvent(
                startedAt,
                agentId,
                agentRole,
                Redaction.RedactSecrets(prompt),
                ContentFormat: "text",
                StreamKind: "prompt"));
        }

        using IDisposable subscription = handle.Session.On(evt =>
        {
            lastEventType = evt.Type;
            Interlocked.Exchange(ref lastEventTicks, DateTimeOffset.UtcNow.UtcTicks);

            string eventType = ResolveEventType(evt);
            sessionContext.SdkEventStream.Publish(CreateRawSdkEvent(evt, handle.Session.SessionId, model, eventType));
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
                case AssistantReasoningDeltaEvent reasoningDelta when !string.IsNullOrWhiteSpace(reasoningDelta.Data.DeltaContent):
                    sessionContext.AgentStream.Publish(new AgentStreamDeltaEvent(
                        DateTimeOffset.UtcNow,
                        agentId,
                        agentRole,
                        reasoningDelta.Data.DeltaContent,
                        ContentFormat: "text",
                        StreamKind: "reasoning"));
                    break;
                case AssistantMessageDeltaEvent delta when !string.IsNullOrWhiteSpace(delta.Data.DeltaContent):
                    completion.Append(delta.Data.DeltaContent);
                    sessionContext.AgentStream.Publish(new AgentStreamDeltaEvent(
                        DateTimeOffset.UtcNow,
                        agentId,
                        agentRole,
                        delta.Data.DeltaContent,
                        ContentFormat: "text",
                        StreamKind: "assistant"));
                    break;
                case AssistantMessageEvent msg when !string.IsNullOrWhiteSpace(msg.Data.Content):
                    finalMessage = msg.Data.Content;
                    break;
                case AssistantTurnEndEvent:
                    // Treat assistant turn end as a lifecycle signal only. The SDK session handle is
                    // reused across delegated steps, so we must still wait for SessionIdleEvent before
                    // returning and allowing the next step to send another prompt.
                    break;
                case ToolExecutionStartEvent toolStart:
                {
                    string? toolName = TryGetToolName(toolStart.Data);
                    if (!string.IsNullOrWhiteSpace(toolName))
                    {
                        string argsJson = TryGetToolArgs(toolStart.Data) ?? "{}";
                        string escapedName = System.Text.Json.JsonSerializer.Serialize(toolName);
                        string message = $"{{\"name\":{escapedName},\"args\":{argsJson}}}";
                        sessionContext.AgentStream.Publish(new AgentStreamDeltaEvent(
                            DateTimeOffset.UtcNow,
                            agentId,
                            agentRole,
                            message,
                            ContentFormat: "text",
                            StreamKind: "tool-call",
                            Title: toolName));
                    }

                    break;
                }
                case SessionErrorEvent err:
                    // Only treat the error as fatal if the SDK did not already
                    // surface it as a recoverable tool-use failure via the
                    // OnErrorOccurred hook (which returns ErrorHandling="continue").
                    // When the error message originates from a tool execution
                    // failure the SDK will continue the agentic loop; completing
                    // with an exception here would abort the turn prematurely.
                    if (!IsRecoverableSessionError(err))
                    {
                        done.TrySetException(new InvalidOperationException($"Copilot SDK session error: {err.Data.Message}"));
                    }

                    break;
                case SessionIdleEvent:
                    done.TrySetResult();
                    break;
            }
        });

        try
        {
            using CancellationTokenRegistration registration = cancellationToken.Register(() => done.TrySetCanceled(cancellationToken));
            Task sendTask = BeginSendAsync(() => handle.Session.SendAsync(new MessageOptions { Prompt = prompt, Mode = "immediate" }));

            await AwaitTurnCompletionAsync(
                sendTask,
                done.Task,
                () => handle.Session.AbortAsync(),
                sessionContext.UserInputState,
                prompt,
                model,
                startedAt,
                () => lastEventType,
                () => Interlocked.Read(ref lastEventTicks),
                timeoutSettings.InactivityTimeoutSeconds,
                timeoutSettings.AbsoluteTimeoutSeconds,
                cancellationToken).ConfigureAwait(false);

            string response = !string.IsNullOrWhiteSpace(finalMessage)
                ? finalMessage
                : completion.ToString().Trim();

            return response;
        }
        finally
        {
            handle.Gate.Release();
        }
    }

    internal static Task AwaitTurnCompletionAsync(
        Task sendTask,
        Task completionTask,
        Func<Task> abortAsync,
        IUserInputState userInputState,
        string prompt,
        string model,
        DateTimeOffset startedAt,
        Func<string> getLastEventType,
        Func<long> getLastEventTicks,
        int inactivityTimeoutSeconds,
        int absoluteTimeoutSeconds,
        CancellationToken cancellationToken)
        => AwaitTurnCompletionCoreAsync(
            sendTask,
            completionTask,
            new SessionTimeoutContext(
                abortAsync,
                userInputState,
                prompt,
                model,
                startedAt,
                getLastEventType,
                getLastEventTicks,
                inactivityTimeoutSeconds,
                absoluteTimeoutSeconds),
            cancellationToken);

    internal static Task BeginSendAsync(Func<Task> sendAsync)
        => Task.Run(sendAsync, CancellationToken.None);

    private static async Task AwaitTurnCompletionCoreAsync(
        Task sendTask,
        Task completionTask,
        SessionTimeoutContext context,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            if (sendTask.IsCompleted)
            {
                await sendTask.ConfigureAwait(false);
            }

            if (completionTask.IsCompleted)
            {
                await completionTask.ConfigureAwait(false);
            }

            if (sendTask.IsCompleted && completionTask.IsCompleted)
            {
                return;
            }

            TimeoutState timeoutState = EvaluateTimeoutState(
                context.StartedAt,
                context.GetLastEventTicks(),
                context.InactivityTimeoutSeconds,
                context.AbsoluteTimeoutSeconds);
            await ThrowIfTimedOutAsync(context, timeoutState).ConfigureAwait(false);

            Task pendingSendTask = sendTask.IsCompleted
                ? Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                : sendTask;
            Task pendingCompletionTask = completionTask.IsCompleted
                ? Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                : completionTask;

            await Task.WhenAny(
                pendingSendTask,
                pendingCompletionTask,
                Task.Delay(timeoutState.WaitDuration, cancellationToken)).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private static TimeoutState EvaluateTimeoutState(
        DateTimeOffset startedAt,
        long lastEventTicks,
        int inactivityTimeoutSeconds,
        int absoluteTimeoutSeconds)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset lastEventAt = new(lastEventTicks, TimeSpan.Zero);

        TimeSpan inactivityRemaining = inactivityTimeoutSeconds > 0
            ? TimeSpan.FromSeconds(inactivityTimeoutSeconds) - (now - lastEventAt)
            : Timeout.InfiniteTimeSpan;
        TimeSpan absoluteRemaining = absoluteTimeoutSeconds > 0
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
        string? timeoutKind = null;
        if (timeoutState.AbsoluteRemaining <= TimeSpan.Zero)
        {
            timeoutKind = $"absolute timeout {context.AbsoluteTimeoutSeconds}s";
        }
        else if (context.InactivityTimeoutSeconds > 0 && timeoutState.InactivityRemaining <= TimeSpan.Zero)
        {
            timeoutKind = $"inactivity timeout {context.InactivityTimeoutSeconds}s";
        }

        if (timeoutKind is null)
        {
            return;
        }

        await context.AbortAsync().ConfigureAwait(false);
        string promptPreview = context.Prompt.Length <= 140 ? context.Prompt : context.Prompt[..137] + "...";
        throw new TimeoutException(
            $"Copilot SDK timed out ({timeoutKind}) for model '{context.Model}'. " +
            $"LastEvent='{context.GetLastEventType()}' at {timeoutState.LastEventAt:HH:mm:ss}. " +
            $"AwaitingUserInput={context.UserInputState.IsAwaitingInput}. Prompt='{promptPreview}'");
    }

    internal static bool IsRecoverableSessionError(SessionErrorEvent err)
    {
        string? errorType = err.Data.ErrorType;
        if (!string.IsNullOrEmpty(errorType)
            && errorType.Contains("tool", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string? message = err.Data.Message;
        return !string.IsNullOrEmpty(message)
            && message.Contains("tool", StringComparison.OrdinalIgnoreCase);
    }

    private static readonly string[] LIFECYCLE_KEYWORDS =
    {
        "session.start", "sessionstart",
        "assistant.turn.end", "assistantturnend",
        "tool.execution.start", "toolexecutionstart",
        "tool.execution.complete", "toolexecutioncomplete",
        "session.compaction.start", "sessioncompactionstart",
        "session.compaction.complete", "sessioncompactioncomplete"
    };

    private static bool IsLifecycleEvent(string eventType)
        => LIFECYCLE_KEYWORDS.Any(kw => eventType.Contains(kw, StringComparison.OrdinalIgnoreCase));

    private static string ResolveEventType(SessionEvent evt)
        => evt.GetType().GetProperty("Type")?.GetValue(evt)?.ToString() ?? evt.GetType().Name;

    private static CopilotSdkRawEvent CreateRawSdkEvent(SessionEvent evt, string sessionId, string model, string eventType)
    {
        DateTimeOffset timestampUtc = DateTimeOffset.UtcNow;
        string eventClass = evt.GetType().FullName ?? evt.GetType().Name;

        try
        {
            string payloadJson = System.Text.Json.JsonSerializer.Serialize(evt, evt.GetType());
            return new CopilotSdkRawEvent(timestampUtc, sessionId, model, eventType, eventClass, payloadJson, null);
        }
        catch (Exception ex)
        {
            return new CopilotSdkRawEvent(timestampUtc, sessionId, model, eventType, eventClass, null, ex.Message);
        }
    }

    private static string? TryGetToolName(object? data)
        => TryGetProperty(data, "ToolName") as string;

    private static string? TryGetToolArgs(object? data)
    {
        object? args = TryGetProperty(data, "Arguments");
        return args is null ? null : System.Text.Json.JsonSerializer.Serialize(args);
    }

    private static object? TryGetProperty(object? data, string propertyName)
    {
        if (data is null)
        {
            return null;
        }

        try
        {
            return data.GetType().GetProperty(propertyName)?.GetValue(data);
        }
        catch
        {
            return null;
        }
    }

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
        Func<Task> AbortAsync,
        IUserInputState UserInputState,
        string Prompt,
        string Model,
        DateTimeOffset StartedAt,
        Func<string> GetLastEventType,
        Func<long> GetLastEventTicks,
        int InactivityTimeoutSeconds,
        int AbsoluteTimeoutSeconds);
}
