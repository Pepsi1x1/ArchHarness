using ArchHarness.App.Constants;
using ArchHarness.App.Core;

namespace ArchHarness.App.Tests.Core;

public sealed class ReplanningSignalBuilderTests
{
    [Fact]
    public void BuildReviewHints_HighArchitectureFinding_TargetsImplementationAgent()
    {
        ArchitectureReview review = new(
            new[] { new ArchitectureFinding(Severities.HIGH, "Layering", "src/app.ts", "handler", "UI calls persistence directly") },
            Array.Empty<string>());
        SecurityReview securityReview = new(Array.Empty<SecurityFinding>(), Array.Empty<string>());

        IReadOnlyList<StepFollowUpHint> hints = ReplanningSignalBuilder.BuildReviewHints(
            review,
            securityReview,
            new[] { "src/app.ts" });

        StepFollowUpHint hint = Assert.Single(hints);
        Assert.Equal(AgentNames.FRONTEND_DEVELOPER, hint.Agent);
        Assert.Contains("Layering", hint.Objective, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UI calls persistence directly", hint.Objective, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildReviewHints_LowFindingsWithoutRequiredActions_ReturnsNoHints()
    {
        ArchitectureReview review = new(
            new[] { new ArchitectureFinding("low", "Naming", "src/service.cs", null, "Could be clearer") },
            Array.Empty<string>());
        SecurityReview securityReview = new(Array.Empty<SecurityFinding>(), Array.Empty<string>());

        IReadOnlyList<StepFollowUpHint> hints = ReplanningSignalBuilder.BuildReviewHints(
            review,
            securityReview,
            new[] { "src/service.cs" });

        Assert.Empty(hints);
    }

    [Fact]
    public void BuildVerificationHints_FailedCriterion_ReferencesOriginalObjective()
    {
        CompletionValidationResult validation = new(
            false,
            new[] { new CriterionResult("Tests pass", false, "dotnet test failed") });
        ClarificationSpec spec = new(
            "Fix tests",
            "All regression tests pass",
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            new[] { "Tests pass" },
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>());
        ExecutionPlan plan = new(
            new[] { new ExecutionPlanStep(1, AgentNames.BACKEND_DEVELOPER, "Fix tests") },
            new IterationStrategy(1, true),
            new[] { "Tests pass" });

        IReadOnlyList<StepFollowUpHint> hints = ReplanningSignalBuilder.BuildVerificationHints(
            validation,
            spec,
            plan,
            null,
            new[] { "src/service.cs" });

        StepFollowUpHint hint = Assert.Single(hints);
        Assert.Equal(AgentNames.BACKEND_DEVELOPER, hint.Agent);
        Assert.Contains("Tests pass", hint.Objective, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("All regression tests pass", hint.Objective, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildVerificationHints_PassedValidation_ReturnsNoHints()
    {
        CompletionValidationResult validation = new(true, Array.Empty<CriterionResult>());
        ExecutionPlan plan = new(Array.Empty<ExecutionPlanStep>(), new IterationStrategy(1, true), Array.Empty<string>());

        IReadOnlyList<StepFollowUpHint> hints = ReplanningSignalBuilder.BuildVerificationHints(
            validation,
            null,
            plan,
            null,
            Array.Empty<string>());

        Assert.Empty(hints);
    }
}
