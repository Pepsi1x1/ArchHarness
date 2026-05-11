using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using ArchHarness.Web.Services;
using GitHub.Copilot.SDK;

namespace ArchHarness.App.Tests.Web;

public sealed class WebInteractionCoordinatorTests
{
    /// <summary>
    /// RequestUserInputAsync — ExposesPendingPromptAndReturnsSubmittedAnswer
    /// </summary>
    [Fact]
    public async Task RequestUserInputAsync_ExposesPendingPromptAndReturnsSubmittedAnswerAsync()
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
        Assert.Null(pending.Questions);

        Assert.True(coordinator.TrySubmitUserInput("Custom answer"));

        UserInputResponse response = await responseTask;
        Assert.Equal("Custom answer", response.Answer);
        Assert.False(state.IsAwaitingInput);
        Assert.Null(coordinator.GetPending());
    }

    /// <summary>
    /// RequestUserInputsAsync — ExposesBatchedQuestionsAndReturnsSubmittedAnswersInOrder
    /// </summary>
    [Fact]
    public async Task RequestUserInputsAsync_ExposesBatchedQuestionsAndReturnsSubmittedAnswersInOrderAsync()
    {
        UserInputState state = new UserInputState();
        WebInteractionCoordinator coordinator = new WebInteractionCoordinator(state);

        Task<IReadOnlyList<UserInputResponse>> responseTask = coordinator.RequestUserInputsAsync(new[]
        {
            new UserInputRequest
            {
                Question = "Which backend should this target?",
                Choices = new List<string>()
            },
            new UserInputRequest
            {
                Question = "Should the plan include migration work?",
                Choices = new List<string>()
            }
        });

        PendingInteractionSnapshot? pending = coordinator.GetPending();
        Assert.NotNull(pending);
        Assert.True(state.IsAwaitingInput);
        Assert.Equal("user-input", pending.Kind);
        Assert.Equal("Answer 2 planning questions to continue.", pending.Question);
        Assert.Empty(pending.Choices);
        Assert.NotNull(pending.Questions);
        Assert.Equal(2, pending.Questions.Count);
        Assert.Equal("Which backend should this target?", pending.Questions[0]);
        Assert.Equal("Should the plan include migration work?", pending.Questions[1]);

        Assert.True(coordinator.TrySubmitUserInputs(new[]
        {
            "Use the existing API.",
            "Yes, include migrations."
        }));

        IReadOnlyList<UserInputResponse> responses = await responseTask;
        Assert.Equal(2, responses.Count);
        Assert.Equal("Use the existing API.", responses[0].Answer);
        Assert.Equal("Yes, include migrations.", responses[1].Answer);
        Assert.False(state.IsAwaitingInput);
        Assert.Null(coordinator.GetPending());
    }

    /// <summary>
    /// RequestPermissionAsync — ExposesPendingPermissionAndReturnsApproval
    /// </summary>
    [Fact]
    public async Task RequestPermissionAsync_ExposesPendingPermissionAndReturnsApprovalAsync()
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

    /// <summary>
    /// RequestPlanApprovalAsync — ExposesPendingApprovalAndReturnsApprovedDecision
    /// </summary>
    [Fact]
    public async Task RequestPlanApprovalAsync_ExposesPendingApprovalAndReturnsApprovedDecisionAsync()
    {
        UserInputState state = new UserInputState();
        WebInteractionCoordinator coordinator = new WebInteractionCoordinator(state);

        ClarificationSpec spec = new(
            "Test task", "Desired outcome", new[] { "In scope" },
            new[] { "Out of scope" }, new[] { "Constraint" }, new[] { "Assumption" },
            Array.Empty<string>(), new[] { "Build passes" }, new[] { "src/app.cs" }, Array.Empty<string>());

        ExecutionPlan plan = new(new[]
        {
            new ExecutionPlanStep(1, "backend-developer", "Implement feature", null, null)
        }, new IterationStrategy(3, true), new[] { "Build passes" });

        PlanApprovalRequest approvalRequest = new(
            spec,
            plan,
            "# Spec\nTask: test",
            "Step 1: Implement feature",
            "## Plan: Test\n\nReview this in chat.",
            "session-1",
            "run-1");
        Task<PlanApprovalResponse> responseTask = coordinator.RequestPlanApprovalAsync(approvalRequest);

        PendingInteractionSnapshot? pending = coordinator.GetPending();
        Assert.NotNull(pending);
        Assert.True(state.IsAwaitingInput);
        Assert.Equal("plan-approval", pending.Kind);
        Assert.Equal("Review the proposed plan in chat, then approve it or describe what should change.", pending.Question);
        Assert.Equal("session-1", pending.SessionId);
        Assert.Equal("run-1", pending.RunId);
        Assert.Equal("# Spec\nTask: test", pending.SpecMarkdown);
        Assert.Equal("Step 1: Implement feature", pending.PlanSummary);
        Assert.Equal("## Plan: Test\n\nReview this in chat.", pending.PlanReviewMarkdown);

        Assert.True(coordinator.TrySubmitPlanApproval(PlanApprovalDecisions.APPROVED, null));

        PlanApprovalResponse response = await responseTask;
        Assert.Equal(PlanApprovalDecisions.APPROVED, response.Decision);
        Assert.Null(response.Reason);
        Assert.False(state.IsAwaitingInput);
        Assert.Null(coordinator.GetPending());
    }

    /// <summary>
    /// RequestPlanApprovalAsync — ReturnsRegenerateDecisionWithReason
    /// </summary>
    [Fact]
    public async Task RequestPlanApprovalAsync_ReturnsRegenerateDecisionWithReasonAsync()
    {
        UserInputState state = new UserInputState();
        WebInteractionCoordinator coordinator = new WebInteractionCoordinator(state);

        ClarificationSpec spec = new(
            "Test task", "Outcome", Array.Empty<string>(),
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

        ExecutionPlan plan = new(new[]
        {
            new ExecutionPlanStep(1, "build", "Build", null, null)
        }, new IterationStrategy(3, true), Array.Empty<string>());

        PlanApprovalRequest approvalRequest = new(spec, plan, "spec md", "plan summary");
        Task<PlanApprovalResponse> responseTask = coordinator.RequestPlanApprovalAsync(approvalRequest);

        Assert.True(coordinator.TrySubmitPlanApproval(PlanApprovalDecisions.REGENERATE, "Add more tests"));

        PlanApprovalResponse response = await responseTask;
        Assert.Equal(PlanApprovalDecisions.REGENERATE, response.Decision);
        Assert.Equal("Add more tests", response.Reason);
    }
}
