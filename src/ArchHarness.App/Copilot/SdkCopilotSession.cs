using System.Text;
using ArchHarness.App.Core;
using GitHub.Copilot.SDK;

namespace ArchHarness.App.Copilot;

internal sealed record SessionIdentity(string? AgentId, string? AgentRole);

internal sealed record SessionTimeoutSettings(int InactivityTimeoutSeconds, int AbsoluteTimeoutSeconds);

internal sealed class SdkSessionEventTracker
{
    private string _lastEventType = "none";
    private long _lastEventTicks;

    public SdkSessionEventTracker(DateTimeOffset startedAt)
        => this._lastEventTicks = startedAt.UtcTicks;

    public string LastEventType => this._lastEventType;

    public long LastEventTicks => Interlocked.Read(ref this._lastEventTicks);

    public void Record(string eventType)
    {
        this._lastEventType = eventType;
        Interlocked.Exchange(ref this._lastEventTicks, DateTimeOffset.UtcNow.UtcTicks);
    }
}

internal sealed class CopilotTurnCompletionMonitor(
    Func<Task> abortAsync,
    IUserInputState userInputState,
    string prompt,
    string model,
    DateTimeOffset startedAt,
    SdkSessionEventTracker eventTracker,
    SessionTimeoutSettings timeoutSettings)
{
    public async Task AwaitCompletionAsync(
        Task sendTask,
        Task completionTask,
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

            TimeoutState timeoutState = this.EvaluateTimeoutState();
            await this.ThrowIfTimedOutAsync(timeoutState).ConfigureAwait(false);

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

    private TimeoutState EvaluateTimeoutState()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset lastEventAt = new(eventTracker.LastEventTicks, TimeSpan.Zero);

        TimeSpan inactivityRemaining = timeoutSettings.InactivityTimeoutSeconds > 0
            ? TimeSpan.FromSeconds(timeoutSettings.InactivityTimeoutSeconds) - (now - lastEventAt)
            : Timeout.InfiniteTimeSpan;
        TimeSpan absoluteRemaining = timeoutSettings.AbsoluteTimeoutSeconds > 0
            ? TimeSpan.FromSeconds(timeoutSettings.AbsoluteTimeoutSeconds) - (now - startedAt)
            : Timeout.InfiniteTimeSpan;

        TimeSpan waitDuration = MinRemaining(inactivityRemaining, absoluteRemaining);
        if (waitDuration != Timeout.InfiniteTimeSpan && waitDuration < TimeSpan.Zero)
        {
            waitDuration = TimeSpan.Zero;
        }

        return new TimeoutState(lastEventAt, inactivityRemaining, absoluteRemaining, waitDuration);
    }

    private static TimeSpan MinRemaining(TimeSpan first, TimeSpan second)
    {
        if (first == Timeout.InfiniteTimeSpan)
        {
            return second;
        }

        if (second == Timeout.InfiniteTimeSpan)
        {
            return first;
        }

        return first < second ? first : second;
    }

    private async Task ThrowIfTimedOutAsync(TimeoutState timeoutState)
    {
        string? timeoutKind = this.ResolveTimeoutKind(timeoutState);
        if (timeoutKind is null)
        {
            return;
        }

        await abortAsync().ConfigureAwait(false);
        string promptPreview = prompt.Length <= 140 ? prompt : prompt[..137] + "...";
        throw new TimeoutException(
            $"Copilot SDK timed out ({timeoutKind}) for model '{model}'. " +
            $"LastEvent='{eventTracker.LastEventType}' at {timeoutState.LastEventAt:HH:mm:ss}. " +
            $"AwaitingUserInput={userInputState.IsAwaitingInput}. Prompt='{promptPreview}'");
    }

    private string? ResolveTimeoutKind(TimeoutState timeoutState)
    {
        if (timeoutState.AbsoluteRemaining <= TimeSpan.Zero)
        {
            return $"absolute timeout {timeoutSettings.AbsoluteTimeoutSeconds}s";
        }

        if (timeoutSettings.InactivityTimeoutSeconds > 0 && timeoutState.InactivityRemaining <= TimeSpan.Zero)
        {
            return $"inactivity timeout {timeoutSettings.InactivityTimeoutSeconds}s";
        }

        return null;
    }

    private sealed record TimeoutState(
        DateTimeOffset LastEventAt,
        TimeSpan InactivityRemaining,
        TimeSpan AbsoluteRemaining,
        TimeSpan WaitDuration);
}

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
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        SdkSessionEventTracker eventTracker = new(startedAt);

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
            eventTracker.Record(evt.Type);

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
            MessageOptions messageOptions = BuildMessageOptions(prompt, options);
            Task sendTask = BeginSendAsync(() => handle.Session.SendAsync(messageOptions));
            CopilotTurnCompletionMonitor turnCompletionMonitor = new(
                () => handle.Session.AbortAsync(),
                sessionContext.UserInputState,
                prompt,
                model,
                startedAt,
                eventTracker,
                timeoutSettings);

            await turnCompletionMonitor.AwaitCompletionAsync(sendTask, done.Task, cancellationToken).ConfigureAwait(false);

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

    internal static Task BeginSendAsync(Func<Task> sendAsync)
        => Task.Run(sendAsync, CancellationToken.None);

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

    internal static MessageOptions BuildMessageOptions(string prompt, CopilotCompletionOptions? options)
    {
        MessageOptions messageOptions = new() { Prompt = prompt, Mode = "immediate" };
        IReadOnlyList<PromptAttachment>? attachments = options?.Attachments;
        if (attachments is null || attachments.Count == 0)
        {
            return messageOptions;
        }

        List<UserMessageAttachment> sdkItems = new(attachments.Count);
        foreach (PromptAttachment attachment in attachments)
        {
            if (string.IsNullOrWhiteSpace(attachment.DataBase64))
            {
                // Only inline blob attachments are transported to the SDK today. Skip references
                // that only carry a StoragePath; callers that persisted attachments must load
                // the payload into DataBase64 before dispatch.
                continue;
            }

            sdkItems.Add(new UserMessageAttachmentBlob
            {
                Data = attachment.DataBase64,
                MimeType = attachment.MimeType,
                DisplayName = attachment.FileName,
            });
        }

        if (sdkItems.Count > 0)
        {
            messageOptions.Attachments = sdkItems;
        }

        return messageOptions;
    }
}
