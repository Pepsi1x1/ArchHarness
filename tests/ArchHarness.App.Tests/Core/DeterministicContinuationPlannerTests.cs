using ArchHarness.App.Constants;
using ArchHarness.App.Core;

namespace ArchHarness.App.Tests.Core;

public sealed class DeterministicContinuationPlannerTests
{
    private static ExecutionPlan BuildPlan(params ExecutionPlanStep[] steps)
        => new ExecutionPlan(steps, new IterationStrategy(2, true), Array.Empty<string>());

    [Fact]
    public void PlanNextWave_NoHints_ReturnsNoSteps()
    {
        DeterministicContinuationPlanner planner = new DeterministicContinuationPlanner();
        ExecutionPlan plan = BuildPlan(new ExecutionPlanStep(1, AgentNames.BACKEND_DEVELOPER, "Implement login"));
        StepOutcome outcome = new StepOutcome(1, AgentNames.BACKEND_DEVELOPER, Array.Empty<string>(), CompletionStatus: StepCompletionStatuses.COMPLETE);

        ContinuationPlanningResult result = planner.PlanNextWave(new ContinuationPlanningContext(
            plan,
            new[] { outcome },
            new[] { outcome },
            Array.Empty<string>(),
            PreviousFilesTouchedCount: 0,
            NextWave: 1,
            NextStepId: 2));

        Assert.Empty(result.NewSteps);
        Assert.Equal("no-hints", result.Reason);
    }

    [Fact]
    public void PlanNextWave_WithFollowUpHint_AppendsStep()
    {
        DeterministicContinuationPlanner planner = new DeterministicContinuationPlanner();
        ExecutionPlan plan = BuildPlan(new ExecutionPlanStep(1, AgentNames.BACKEND_DEVELOPER, "Implement login"));
        StepFollowUpHint hint = new StepFollowUpHint(
            AgentNames.FRONTEND_DEVELOPER,
            "Wire login form to the new endpoint",
            "Backend contract added");
        StepOutcome outcome = new StepOutcome(
            1,
            AgentNames.BACKEND_DEVELOPER,
            new[] { "server/auth.cs" },
            CompletionStatus: StepCompletionStatuses.PARTIAL,
            UnresolvedWork: new[] { "frontend integration" },
            FollowUpHints: new[] { hint });

        ContinuationPlanningResult result = planner.PlanNextWave(new ContinuationPlanningContext(
            plan,
            new[] { outcome },
            new[] { outcome },
            new[] { "server/auth.cs" },
            PreviousFilesTouchedCount: 0,
            NextWave: 1,
            NextStepId: 2));

        Assert.Single(result.NewSteps);
        ExecutionPlanStep appended = result.NewSteps[0];
        Assert.Equal(AgentNames.FRONTEND_DEVELOPER, appended.Agent);
        Assert.Equal("Wire login form to the new endpoint", appended.Objective);
        Assert.Equal(2, appended.Id);
        Assert.Equal(1, appended.Wave);
        Assert.StartsWith("continuation", appended.OriginHint);
    }

    [Fact]
    public void PlanNextWave_HintDuplicatesExistingStep_Skipped()
    {
        DeterministicContinuationPlanner planner = new DeterministicContinuationPlanner();
        ExecutionPlan plan = BuildPlan(
            new ExecutionPlanStep(1, AgentNames.FRONTEND_DEVELOPER, "Wire login form"),
            new ExecutionPlanStep(2, AgentNames.BACKEND_DEVELOPER, "Implement login"));
        StepFollowUpHint duplicateHint = new StepFollowUpHint(
            AgentNames.FRONTEND_DEVELOPER,
            "Wire login form",
            "duplicate");
        StepOutcome outcome = new StepOutcome(
            2,
            AgentNames.BACKEND_DEVELOPER,
            Array.Empty<string>(),
            FollowUpHints: new[] { duplicateHint });

        ContinuationPlanningResult result = planner.PlanNextWave(new ContinuationPlanningContext(
            plan,
            new[] { outcome },
            new[] { outcome },
            Array.Empty<string>(),
            PreviousFilesTouchedCount: 0,
            NextWave: 1,
            NextStepId: 3));

        Assert.Empty(result.NewSteps);
        Assert.Equal("hints-were-duplicates", result.Reason);
    }

    [Fact]
    public void PlanNextWave_UnknownAgent_Skipped()
    {
        DeterministicContinuationPlanner planner = new DeterministicContinuationPlanner();
        ExecutionPlan plan = BuildPlan(new ExecutionPlanStep(1, AgentNames.BACKEND_DEVELOPER, "Implement"));
        StepOutcome outcome = new StepOutcome(
            1,
            AgentNames.BACKEND_DEVELOPER,
            Array.Empty<string>(),
            FollowUpHints: new[] { new StepFollowUpHint("UnknownAgent", "Do something", "reason") });

        ContinuationPlanningResult result = planner.PlanNextWave(new ContinuationPlanningContext(
            plan,
            new[] { outcome },
            new[] { outcome },
            Array.Empty<string>(),
            PreviousFilesTouchedCount: 0,
            NextWave: 1,
            NextStepId: 2));

        Assert.Empty(result.NewSteps);
    }
}
