namespace ArchHarness.App.Core;

public sealed record RunRequest(
    string TaskPrompt,
    string WorkspacePath,
    string WorkspaceMode,
    string Workflow,
    string? ProjectName,
    IDictionary<string, string>? ModelOverrides,
    string? BuildCommand
);

public sealed record ExecutionPlanStep(
    int Id,
    string Agent,
    string Objective,
    IReadOnlyList<int>? DependsOnStepIds = null,
    IReadOnlyList<string>? Languages = null);

public sealed record IterationStrategy(int MaxIterations, bool ReviewRequired);

public sealed record ExecutionPlan(
    IReadOnlyList<ExecutionPlanStep> Steps,
    IterationStrategy IterationStrategy,
    IReadOnlyList<string> CompletionCriteria
);

public sealed record ArchitectureFinding(string Severity, string Rule, string? File, string? Symbol, string Rationale);

public sealed record ArchitectureReview(IReadOnlyList<ArchitectureFinding> Findings, IReadOnlyList<string> RequiredActions);

public sealed record ArchitectureReviewRequest(
    string DelegatedPrompt,
    string Diff,
    string WorkspaceRoot,
    IReadOnlyList<string> FilesTouched,
    IReadOnlyList<string>? LanguageScope,
    IDictionary<string, string>? ModelOverrides);

public sealed record StyleEnforcementRequest(
    string DelegatedPrompt,
    string Diff,
    string WorkspaceRoot,
    IReadOnlyList<string> FilesTouched,
    IReadOnlyList<string>? LanguageScope,
    IDictionary<string, string>? ModelOverrides);

public sealed record CompletionValidationRequest(
    ExecutionPlan Plan,
    ArchitectureReview Review,
    bool BuildPassed,
    bool BuildCommandConfigured,
    IDictionary<string, string>? ModelOverrides);

public sealed record ArchitectureLoopRequest(
    IterationStrategy IterationStrategy,
    ArchitectureReview InitialReview,
    IReadOnlyList<string> FilesTouched,
    IReadOnlyList<string>? ArchitectureLanguages,
    RunRequest RunRequest);

public sealed record RunArtefacts(string RunId, string RunDirectory);

public sealed record CopilotModelUsage(string Model, int Calls, int PromptCharacters, int CompletionCharacters);

public sealed record RuntimeProgressEvent(DateTimeOffset TimestampUtc, string Source, string Message, string? Prompt = null);

public sealed record AgentStreamDeltaEvent(
    DateTimeOffset TimestampUtc,
    string AgentId,
    string AgentRole,
    string DeltaContent);
