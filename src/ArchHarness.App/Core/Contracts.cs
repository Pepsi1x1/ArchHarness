namespace ArchHarness.App.Core;

public sealed record RunRequest(
    string TaskPrompt,
    string WorkspacePath,
    string WorkspaceMode,
    string Workflow,
    string? ProjectName,
    IDictionary<string, string>? ModelOverrides
);

public sealed record ExecutionPlanStep(int Id, string Agent, string Objective);

public sealed record IterationStrategy(int MaxIterations, bool ReviewRequired);

public sealed record ExecutionPlan(
    IReadOnlyList<ExecutionPlanStep> Steps,
    IterationStrategy IterationStrategy,
    IReadOnlyList<string> CompletionCriteria
);

public sealed record ArchitectureFinding(string Severity, string Rule, string? File, string? Symbol, string Rationale);

public sealed record ArchitectureReview(IReadOnlyList<ArchitectureFinding> Findings, IReadOnlyList<string> RequiredActions);

public sealed record RunArtefacts(string RunId, string RunDirectory);
