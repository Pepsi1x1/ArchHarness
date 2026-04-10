using ArchHarness.App.Copilot;
using GitHub.Copilot.SDK;

namespace ArchHarness.App.Tests.Copilot;

public sealed class CopilotSessionFactoryTests
{
    [Fact]
    public void BuildErrorOccurredDetails_UsesStableMetadataOnly()
    {
        ErrorOccurredHookInput input = new ErrorOccurredHookInput
        {
            ErrorContext = "model_call",
            Recoverable = true,
            Error = "transport dropped"
        };

        string details = CopilotSessionFactory.BuildErrorOccurredDetails(input);

        Assert.Contains("context=model_call", details);
        Assert.Contains("recoverable=True", details);
        Assert.Contains("errorType=System.String", details);
        Assert.DoesNotContain("transport dropped", details);
    }

    [Fact]
    public void BuildPostToolUseDetails_UsesOnlyToolName()
    {
        PostToolUseHookInput input = new PostToolUseHookInput
        {
            ToolName = "apply_patch",
            ToolArgs = "*** Begin Patch\n*** Update File: huge unstable content that could deadlock the SDK"
        };

        string details = CopilotSessionFactory.BuildPostToolUseDetails(input);

        Assert.Equal("tool=apply_patch", details);
        Assert.DoesNotContain("Begin Patch", details);
        Assert.DoesNotContain("unstable", details);
    }

    [Fact]
    public void BuildPostToolUseDetails_NullToolName_ReturnsUnknown()
    {
        PostToolUseHookInput input = new PostToolUseHookInput();

        string details = CopilotSessionFactory.BuildPostToolUseDetails(input);

        Assert.Equal("tool=unknown", details);
    }
}
