using ArchHarness.App.Copilot;
using GitHub.Copilot.SDK;

namespace ArchHarness.App.Tests.Copilot;

public sealed class CopilotGovernancePolicyTests
{
    [Fact]
    public async Task OnPostToolUseAsync_DerivedFailureInput_SkipsLoggerAndReturnsGenericContext()
    {
        ThrowingToolUsageLogger logger = new ThrowingToolUsageLogger();
        CopilotGovernancePolicy policy = new CopilotGovernancePolicy(logger);

        FailurePostToolUseHookInput input = new FailurePostToolUseHookInput
        {
            ToolName = "web_fetch",
            Error = "Error: Failed to fetch - status code 404"
        };

        PostToolUseHookOutput output = await policy.OnPostToolUseAsync(input);

        Assert.False(logger.LogPostCalled);
        Assert.Equal("Tool failure observed under governance audit.", output.AdditionalContext);
    }

    [Fact]
    public async Task OnPostToolUseAsync_SuccessInput_LogsAndReturnsToolSpecificContext()
    {
        RecordingToolUsageLogger logger = new RecordingToolUsageLogger();
        CopilotGovernancePolicy policy = new CopilotGovernancePolicy(logger);

        PostToolUseHookInput input = new PostToolUseHookInput
        {
            ToolName = "report_intent"
        };

        PostToolUseHookOutput output = await policy.OnPostToolUseAsync(input);

        Assert.True(logger.LogPostCalled);
        Assert.Equal("Tool 'report_intent' completed under governance audit.", output.AdditionalContext);
    }

    private sealed class FailurePostToolUseHookInput : PostToolUseHookInput
    {
        public string? Error { get; set; }
    }

    private sealed class ThrowingToolUsageLogger : IToolUsageLogger
    {
        public bool LogPostCalled { get; private set; }

        public Task LogPreToolUseAsync(PreToolUseHookInput input, string decision, bool deniedByName, bool deniedByArgs)
            => Task.CompletedTask;

        public Task LogPostToolUseAsync(PostToolUseHookInput input)
        {
            this.LogPostCalled = true;
            throw new InvalidOperationException("Failure post hooks should bypass the logger.");
        }
    }

    private sealed class RecordingToolUsageLogger : IToolUsageLogger
    {
        public bool LogPostCalled { get; private set; }

        public Task LogPreToolUseAsync(PreToolUseHookInput input, string decision, bool deniedByName, bool deniedByArgs)
            => Task.CompletedTask;

        public Task LogPostToolUseAsync(PostToolUseHookInput input)
        {
            this.LogPostCalled = true;
            return Task.CompletedTask;
        }
    }
}
