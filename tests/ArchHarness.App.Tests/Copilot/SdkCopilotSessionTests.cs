using ArchHarness.App.Copilot;
using GitHub.Copilot.SDK;

namespace ArchHarness.App.Tests.Copilot;

public sealed class SdkCopilotSessionTests
{
    [Fact]
    public async Task AwaitTurnCompletionAsync_SendTaskStalls_ThrowsTimeoutAndAbortsSession()
    {
        TaskCompletionSource send = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        DateTimeOffset startedAt = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(2);
        DateTimeOffset lastEventAt = startedAt;
        int abortCount = 0;

        TimeoutException exception = await Assert.ThrowsAsync<TimeoutException>(() => SdkCopilotSession.AwaitTurnCompletionAsync(
            send.Task,
            completion.Task,
            () =>
            {
                Interlocked.Increment(ref abortCount);
                return Task.CompletedTask;
            },
            new UserInputState(),
            "Investigate env API",
            "gpt-5.4",
            startedAt,
            () => "report_intent",
            () => lastEventAt.UtcTicks,
            inactivityTimeoutSeconds: 0,
            absoluteTimeoutSeconds: 1,
            CancellationToken.None));

        Assert.Equal(1, abortCount);
        Assert.Contains("absolute timeout 1s", exception.Message);
        Assert.Contains("LastEvent='report_intent'", exception.Message);
    }

    [Fact]
    public async Task AwaitTurnCompletionAsync_SynchronouslyBlockingSendInvocation_StillTimesOut()
    {
        using ManualResetEventSlim releaseSend = new(false);
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        DateTimeOffset startedAt = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(2);
        DateTimeOffset lastEventAt = startedAt;
        int abortCount = 0;

        Task sendTask = SdkCopilotSession.BeginSendAsync(() =>
        {
            releaseSend.Wait();
            return Task.CompletedTask;
        });

        TimeoutException exception = await Assert.ThrowsAsync<TimeoutException>(() => SdkCopilotSession.AwaitTurnCompletionAsync(
            sendTask,
            completion.Task,
            () =>
            {
                Interlocked.Increment(ref abortCount);
                return Task.CompletedTask;
            },
            new UserInputState(),
            "Investigate env API",
            "gpt-5.4",
            startedAt,
            () => "report_intent",
            () => lastEventAt.UtcTicks,
            inactivityTimeoutSeconds: 0,
            absoluteTimeoutSeconds: 1,
            CancellationToken.None));

        releaseSend.Set();
        await sendTask;

        Assert.Equal(1, abortCount);
        Assert.Contains("absolute timeout 1s", exception.Message);
    }

    [Theory]
    [InlineData("tool_error", null, true)]
    [InlineData("tool_execution_error", null, true)]
    [InlineData(null, "Tool 'sql' failed: FOREIGN KEY constraint failed", true)]
    [InlineData("model_error", "rate limit exceeded", false)]
    [InlineData(null, "session disconnected", false)]
    [InlineData(null, null, false)]
    public void IsRecoverableSessionError_ClassifiesCorrectly(string? errorType, string? message, bool expected)
    {
        SessionErrorEvent evt = new()
        {
            Data = new SessionErrorData
            {
                ErrorType = errorType,
                Message = message
            }
        };

        bool result = SdkCopilotSession.IsRecoverableSessionError(evt);

        Assert.Equal(expected, result);
    }
}
