using ArchHarness.App.Core;

namespace ArchHarness.App.Tests.Core;

public sealed class AgentStepExecutorBatchTests
{
    [Fact]
    public void ResolveDependencyReadyBatch_NoDependencies_ReturnsAllPendingSteps()
    {
        Dictionary<int, ExecutionPlanStep> pending = new Dictionary<int, ExecutionPlanStep>()
        {
            [1] = new ExecutionPlanStep(1, "backend-developer", "Implement A", null, null),
            [2] = new ExecutionPlanStep(2, "frontend-developer", "Implement B", null, null),
            [3] = new ExecutionPlanStep(3, "build", "Build project", null, null)
        };
        HashSet<int> completed = new HashSet<int>();

        List<ExecutionPlanStep> batch = AgentStepExecutor.ResolveDependencyReadyBatch(pending, completed);

        Assert.Equal(3, batch.Count);
        Assert.Equal(new[] { 1, 2, 3 }, batch.Select(s => s.Id));
    }

    [Fact]
    public void ResolveDependencyReadyBatch_WithDependencies_ReturnsOnlyReadySteps()
    {
        Dictionary<int, ExecutionPlanStep> pending = new Dictionary<int, ExecutionPlanStep>()
        {
            [1] = new ExecutionPlanStep(1, "backend-developer", "Implement A", null, null),
            [2] = new ExecutionPlanStep(2, "frontend-developer", "Implement B", null, null),
            [3] = new ExecutionPlanStep(3, "build", "Build project", new List<int> { 1, 2 }, null)
        };
        HashSet<int> completed = new HashSet<int>();

        List<ExecutionPlanStep> batch = AgentStepExecutor.ResolveDependencyReadyBatch(pending, completed);

        Assert.Equal(2, batch.Count);
        Assert.Equal(new[] { 1, 2 }, batch.Select(s => s.Id));
    }

    [Fact]
    public void ResolveDependencyReadyBatch_AllDependenciesMet_ReleasesDependentStep()
    {
        Dictionary<int, ExecutionPlanStep> pending = new Dictionary<int, ExecutionPlanStep>()
        {
            [3] = new ExecutionPlanStep(3, "build", "Build project", new List<int> { 1, 2 }, null)
        };
        HashSet<int> completed = new HashSet<int>() { 1, 2 };

        List<ExecutionPlanStep> batch = AgentStepExecutor.ResolveDependencyReadyBatch(pending, completed);

        Assert.Single(batch);
        Assert.Equal(3, batch[0].Id);
    }

    [Fact]
    public void ResolveDependencyReadyBatch_Deadlock_ReturnsEmptyBatch()
    {
        Dictionary<int, ExecutionPlanStep> pending = new Dictionary<int, ExecutionPlanStep>()
        {
            [1] = new ExecutionPlanStep(1, "backend-developer", "A", new List<int> { 2 }, null),
            [2] = new ExecutionPlanStep(2, "frontend-developer", "B", new List<int> { 1 }, null)
        };
        HashSet<int> completed = new HashSet<int>();

        List<ExecutionPlanStep> batch = AgentStepExecutor.ResolveDependencyReadyBatch(pending, completed);

        Assert.Empty(batch);
    }

    [Fact]
    public void ResolveDependencyReadyBatch_PartialCompletion_ReturnsNewlyReadySteps()
    {
        Dictionary<int, ExecutionPlanStep> pending = new Dictionary<int, ExecutionPlanStep>()
        {
            [2] = new ExecutionPlanStep(2, "frontend-developer", "B", new List<int> { 1 }, null),
            [3] = new ExecutionPlanStep(3, "build", "C", new List<int> { 1, 2 }, null)
        };
        HashSet<int> completed = new HashSet<int>() { 1 };

        List<ExecutionPlanStep> batch = AgentStepExecutor.ResolveDependencyReadyBatch(pending, completed);

        Assert.Single(batch);
        Assert.Equal(2, batch[0].Id);
    }

    [Fact]
    public void MergeOutcome_FilesTouchedDelta_MergesDistinctFiles()
    {
        AgentStepExecutor.ExecutionState state = new AgentStepExecutor.ExecutionState()`r`n        {
            FilesTouched = new[] { "src/A.cs", "src/B.cs" }
        };

        AgentStepExecutor.MergeOutcome(state, new StepOutcome(1, "backend-developer",
            new[] { "src/B.cs", "src/C.cs" }));

        Assert.Equal(3, state.FilesTouched.Count);
        Assert.Contains("src/A.cs", state.FilesTouched);
        Assert.Contains("src/C.cs", state.FilesTouched);
    }

    [Fact]
    public void MergeOutcome_FrontendPlanDelta_OverwritesPlan()
    {
        AgentStepExecutor.ExecutionState state = new AgentStepExecutor.ExecutionState() { FrontendPlan = "Old" };

        AgentStepExecutor.MergeOutcome(state, new StepOutcome(1, "frontend-developer",
            Array.Empty<string>(), FrontendPlanDelta: "New frontend plan"));

        Assert.Equal("New frontend plan", state.FrontendPlan);
    }

    [Fact]
    public void MergeOutcome_ArchitectureReview_SetsReview()
    {
        AgentStepExecutor.ExecutionState state = new AgentStepExecutor.ExecutionState();
        ArchitectureReview review = new(new[] { new ArchitectureFinding("High", "Issue", null, null, "Fix it") }, Array.Empty<string>());

        AgentStepExecutor.MergeOutcome(state, new StepOutcome(1, "architecture",
            Array.Empty<string>(), Review: review));

        Assert.Same(review, state.Review);
    }

    [Fact]
    public void MergeOutcome_SecurityReview_SetsReview()
    {
        AgentStepExecutor.ExecutionState state = new AgentStepExecutor.ExecutionState();
        SecurityReview security = new(new[] { new SecurityFinding("Critical", "SQL Injection", null, null, "Use parameterized queries", "A03:2021") }, Array.Empty<string>());

        AgentStepExecutor.MergeOutcome(state, new StepOutcome(1, "security",
            Array.Empty<string>(), SecurityReview: security));

        Assert.Same(security, state.SecurityReview);
    }

    [Fact]
    public void MergeOutcome_BuildOutcome_SetsLastBuildOutcome()
    {
        AgentStepExecutor.ExecutionState state = new AgentStepExecutor.ExecutionState();
        BuildOutcome build = new(true, "Build succeeded", 5, DateTimeOffset.UtcNow);

        AgentStepExecutor.MergeOutcome(state, new StepOutcome(5, "build",
            Array.Empty<string>(), BuildOutcome: build));

        Assert.Same(build, state.LastBuildOutcome);
    }

    [Fact]
    public void MergeOutcome_NullDeltas_DoesNotOverwrite()
    {
        ArchitectureReview existingReview = new(Array.Empty<ArchitectureFinding>(), Array.Empty<string>());
        AgentStepExecutor.ExecutionState state = new AgentStepExecutor.ExecutionState()`r`n        {
            FrontendPlan = "Existing",
            Review = existingReview
        };

        AgentStepExecutor.MergeOutcome(state, new StepOutcome(1, "backend-developer",
            Array.Empty<string>()));

        Assert.Equal("Existing", state.FrontendPlan);
        Assert.Same(existingReview, state.Review);
        Assert.Null(state.LastBuildOutcome);
    }
}
