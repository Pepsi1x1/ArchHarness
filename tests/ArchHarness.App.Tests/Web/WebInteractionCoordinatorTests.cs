using ArchHarness.App.Copilot;
using ArchHarness.Web.Services;
using GitHub.Copilot.SDK;

namespace ArchHarness.App.Tests.Web;

public sealed class WebInteractionCoordinatorTests
{
    [Fact]
    public async Task RequestUserInputAsync_ExposesPendingPromptAndReturnsSubmittedAnswer()
    {
        UserInputState state = new UserInputState();
        WebInteractionCoordinator coordinator = new WebInteractionCoordinator(state);

        Task<UserInputResponse> responseTask = coordinator.RequestUserInputAsync(new UserInputRequest
        {
            Question = "Choose a path",
            Choices = new List<string> { "Option A", "Option B" }
        });

        PendingInteractionSnapshot? pending = coordinator.GetPending();
        Assert.NotNull(pending);
        Assert.True(state.IsAwaitingInput);
        Assert.Equal("user-input", pending.Kind);
        Assert.Equal("Choose a path", pending.Question);
        Assert.Equal(2, pending.Choices.Count);

        Assert.True(coordinator.TrySubmitUserInput("Custom answer"));

        UserInputResponse response = await responseTask;
        Assert.Equal("Custom answer", response.Answer);
        Assert.False(state.IsAwaitingInput);
        Assert.Null(coordinator.GetPending());
    }

    [Fact]
    public async Task RequestPermissionAsync_ExposesPendingPermissionAndReturnsApproval()
    {
        UserInputState state = new UserInputState();
        WebInteractionCoordinator coordinator = new WebInteractionCoordinator(state);

        Task<PermissionRequestResult> responseTask = coordinator.RequestPermissionAsync(
            new PermissionRequestCustomTool
            {
                ToolName = "apply_patch",
                ToolDescription = "Apply a workspace patch"
            },
            new PermissionInvocation
            {
                SessionId = "session-123"
            });

        PendingInteractionSnapshot? pending = coordinator.GetPending();
        Assert.NotNull(pending);
        Assert.True(state.IsAwaitingInput);
        Assert.Equal("permission", pending.Kind);
        Assert.Equal("session-123", pending.SessionId);
        Assert.Equal("apply_patch", pending.ToolName);

        Assert.True(coordinator.TrySubmitPermission(approved: true));

        PermissionRequestResult result = await responseTask;
        Assert.Equal(PermissionRequestResultKind.Approved, result.Kind);
        Assert.False(state.IsAwaitingInput);
        Assert.Null(coordinator.GetPending());
    }
}