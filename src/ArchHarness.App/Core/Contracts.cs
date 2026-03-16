namespace ArchHarness.App.Core;

/// <summary>
/// Represents an incoming request to execute an orchestrated run.
/// </summary>
/// <param name="TaskPrompt">The user-supplied task description that drives planning and execution.</param>
/// <param name="WorkspacePath">The file-system path to the target workspace.</param>
/// <param name="WorkspaceMode">The workspace initialization mode (e.g., "new-project", "existing-git").</param>
/// <param name="Workflow">The workflow identifier that selects the execution pipeline.</param>
/// <param name="ProjectName">Optional project name used when creating a new workspace.</param>
/// <param name="ModelOverrides">Optional per-agent model override mappings.</param>
/// <param name="BuildCommand">Optional build command to execute for validation.</param>
/// <param name="PermissionHandlerMode">Permission approval mode used for Copilot session tool requests.</param>
/// <param name="ReviewLoopAgents">Optional per-run review-loop agent selection override.</param>
/// <param name="ArchitectureLoopMode">When true, enables iterative architecture review over the entire workspace.</param>
/// <param name="ArchitectureLoopPrompt">Optional supplementary prompt applied during architecture loop iterations.</param>
/// <param name="ProjectId">Optional stable project identifier associated with the run workspace.</param>
/// <param name="RunTitle">Optional human-friendly title for the run.</param>
public sealed record RunRequest(
    string TaskPrompt,
    string WorkspacePath,
    string WorkspaceMode,
    string Workflow,
    string? ProjectName,
    IDictionary<string, string>? ModelOverrides,
    string? BuildCommand,
    string PermissionHandlerMode = PermissionHandlerModes.APPROVE_ALL,
    ReviewLoopAgentSelection? ReviewLoopAgents = null,
    bool ArchitectureLoopMode = false,
    string? ArchitectureLoopPrompt = null,
    string? ProjectId = null,
    string? RunTitle = null
);

/// <summary>
/// A single step within an execution plan, targeting a specific agent with a concrete objective.
/// </summary>
/// <param name="Id">The unique numeric identifier of this step within the plan.</param>
/// <param name="Agent">The agent role responsible for executing this step (e.g., "BackendDeveloper", "Architecture").</param>
/// <param name="Objective">The delegated prompt that the target agent will execute.</param>
/// <param name="DependsOnStepIds">Optional list of step IDs that must complete before this step can start.</param>
/// <param name="Languages">Optional language scope for review/enforcement steps (e.g., "dotnet", "vue3").</param>
public sealed record ExecutionPlanStep(
    int Id,
    string Agent,
    string Objective,
    IReadOnlyList<int>? DependsOnStepIds = null,
    IReadOnlyList<string>? Languages = null);

/// <summary>
/// Controls how many remediation iterations are allowed and whether review is required.
/// </summary>
/// <param name="MaxIterations">The maximum number of review-remediation iterations to perform.</param>
/// <param name="ReviewRequired">Whether architecture review must pass before the run is considered complete.</param>
public sealed record IterationStrategy(int MaxIterations, bool ReviewRequired);

/// <summary>
/// A complete execution plan comprising ordered steps, an iteration strategy, and completion criteria.
/// </summary>
/// <param name="Steps">The ordered list of execution plan steps.</param>
/// <param name="IterationStrategy">Controls review iteration behavior and limits.</param>
/// <param name="CompletionCriteria">Conditions that must be met for the run to be considered complete.</param>
public sealed record ExecutionPlan(
    IReadOnlyList<ExecutionPlanStep> Steps,
    IterationStrategy IterationStrategy,
    IReadOnlyList<string> CompletionCriteria
);

/// <summary>
/// A single finding from an architecture review, identifying a rule violation in the codebase.
/// </summary>
/// <param name="Severity">The severity level of the finding (e.g., "high", "medium", "low").</param>
/// <param name="Rule">The architecture rule that was violated.</param>
/// <param name="File">The file where the violation was found, or null if workspace-wide.</param>
/// <param name="Symbol">The code symbol associated with the violation, or null if not applicable.</param>
/// <param name="Rationale">Explanation of why this constitutes a violation.</param>
public sealed record ArchitectureFinding(string Severity, string Rule, string? File, string? Symbol, string Rationale);

/// <summary>
/// The result of an architecture review, containing all findings and required remediation actions.
/// </summary>
/// <param name="Findings">The list of architecture findings identified during review.</param>
/// <param name="RequiredActions">Actions that must be taken to remediate the findings.</param>
public sealed record ArchitectureReview(IReadOnlyList<ArchitectureFinding> Findings, IReadOnlyList<string> RequiredActions);

/// <summary>
/// A single finding from a security review, identifying a security vulnerability in the codebase.
/// </summary>
/// <param name="Severity">The severity level of the finding (e.g., "high", "medium", "low").</param>
/// <param name="Rule">The security rule that was violated.</param>
/// <param name="File">The file where the vulnerability was found, or null if workspace-wide.</param>
/// <param name="Symbol">The code symbol associated with the vulnerability, or null if not applicable.</param>
/// <param name="Rationale">Explanation of why this constitutes a security vulnerability.</param>
/// <param name="OwaspCategory">The OWASP category this vulnerability falls under.</param>
public sealed record SecurityFinding(string Severity, string Rule, string? File, string? Symbol, string Rationale, string OwaspCategory);

/// <summary>
/// The result of a security review, containing all findings and required remediation actions.
/// </summary>
/// <param name="Findings">The list of security findings identified during review.</param>
/// <param name="RequiredActions">Actions that must be taken to remediate the security findings.</param>
public sealed record SecurityReview(IReadOnlyList<SecurityFinding> Findings, IReadOnlyList<string> RequiredActions);

/// <summary>
/// Request payload for an architecture review pass over workspace changes.
/// </summary>
/// <param name="DelegatedPrompt">The prompt describing review scope and expectations.</param>
/// <param name="Diff">The git diff of changes to review.</param>
/// <param name="WorkspaceRoot">The root path of the workspace under review.</param>
/// <param name="FilesTouched">Files that were created or modified during execution.</param>
/// <param name="LanguageScope">Optional language filter for scoping the review.</param>
/// <param name="ModelOverrides">Optional per-agent model override mappings.</param>
public sealed record ArchitectureReviewRequest(
    string DelegatedPrompt,
    string Diff,
    string WorkspaceRoot,
    IReadOnlyList<string> FilesTouched,
    IReadOnlyList<string>? LanguageScope,
    IDictionary<string, string>? ModelOverrides);

/// <summary>
/// Request payload for a security review pass over workspace changes.
/// </summary>
/// <param name="DelegatedPrompt">The prompt describing review scope and expectations.</param>
/// <param name="Diff">The git diff of changes to review.</param>
/// <param name="WorkspaceRoot">The root path of the workspace under review.</param>
/// <param name="FilesTouched">Files that were created or modified during execution.</param>
/// <param name="LanguageScope">Optional language filter for scoping the review.</param>
/// <param name="ModelOverrides">Optional per-agent model override mappings.</param>
public sealed record SecurityReviewRequest(
    string DelegatedPrompt,
    string Diff,
    string WorkspaceRoot,
    IReadOnlyList<string> FilesTouched,
    IReadOnlyList<string>? LanguageScope,
    IDictionary<string, string>? ModelOverrides);

/// <summary>
/// Request payload for a coding style enforcement pass over workspace changes.
/// </summary>
/// <param name="DelegatedPrompt">The prompt describing style enforcement scope.</param>
/// <param name="Diff">The git diff of changes to enforce style on.</param>
/// <param name="WorkspaceRoot">The root path of the workspace under review.</param>
/// <param name="FilesTouched">Files that were created or modified during execution.</param>
/// <param name="LanguageScope">Optional language filter for scoping enforcement.</param>
/// <param name="ModelOverrides">Optional per-agent model override mappings.</param>
public sealed record StyleEnforcementRequest(
    string DelegatedPrompt,
    string Diff,
    string WorkspaceRoot,
    IReadOnlyList<string> FilesTouched,
    IReadOnlyList<string>? LanguageScope,
    IDictionary<string, string>? ModelOverrides);

/// <summary>
/// Request payload for validating whether a run has met its completion criteria.
/// </summary>
/// <param name="Plan">The execution plan that was executed.</param>
/// <param name="Review">The final architecture review result.</param>
/// <param name="SecurityReview">The final security review result.</param>
/// <param name="ModelOverrides">Optional per-agent model override mappings.</param>
public sealed record CompletionValidationRequest(
    ExecutionPlan Plan,
    ArchitectureReview Review,
    SecurityReview SecurityReview,
    IDictionary<string, string>? ModelOverrides);

/// <summary>
/// Request payload for the architecture review remediation loop.
/// </summary>
/// <param name="IterationStrategy">Controls review iteration behavior and limits.</param>
/// <param name="InitialReview">The architecture review from the initial execution pass.</param>
/// <param name="InitialSecurityReview">The security review from the initial execution pass.</param>
/// <param name="FilesTouched">Files that were created or modified during execution.</param>
/// <param name="ArchitectureLanguages">Optional language scope for architecture review.</param>
/// <param name="SecurityLanguages">Optional language scope for security review.</param>
/// <param name="RunRequest">The originating run request.</param>
public sealed record ArchitectureLoopRequest(
    IterationStrategy IterationStrategy,
    ArchitectureReview InitialReview,
    SecurityReview InitialSecurityReview,
    IReadOnlyList<string> FilesTouched,
    IReadOnlyList<string>? ArchitectureLanguages,
    IReadOnlyList<string>? SecurityLanguages,
    RunRequest RunRequest);

/// <summary>
/// Identifies the artifacts produced by a completed run.
/// </summary>
/// <param name="RunId">The unique timestamped identifier of the run.</param>
/// <param name="RunDirectory">The full file-system path to the run's artifact directory.</param>
public sealed record RunArtefacts(string RunId, string RunDirectory);

/// <summary>
/// Tracks Copilot model usage statistics for a single model during a run.
/// </summary>
/// <param name="Model">The model identifier.</param>
/// <param name="Calls">The total number of completion calls made to this model.</param>
/// <param name="PromptCharacters">The total number of prompt characters sent.</param>
/// <param name="CompletionCharacters">The total number of completion characters received.</param>
public sealed record CopilotModelUsage(string Model, int Calls, int PromptCharacters, int CompletionCharacters);

/// <summary>
/// Reports runtime progress to observers during an orchestrated run.
/// </summary>
/// <param name="TimestampUtc">The UTC timestamp when the event occurred.</param>
/// <param name="Source">The component or agent that generated the event.</param>
/// <param name="Message">A human-readable progress message.</param>
/// <param name="Prompt">Optional prompt text associated with the event for diagnostics.</param>
public sealed record RuntimeProgressEvent(DateTimeOffset TimestampUtc, string Source, string Message, string? Prompt = null);

/// <summary>
/// Represents a streaming content delta from an agent during execution.
/// </summary>
/// <param name="TimestampUtc">The UTC timestamp when the delta was produced.</param>
/// <param name="AgentId">The unique identifier of the agent that produced this delta.</param>
/// <param name="AgentRole">The role of the agent (e.g., "backend-developer", "architecture").</param>
/// <param name="DeltaContent">The incremental content fragment.</param>
/// <param name="ContentFormat">The content format, typically <c>text</c> or <c>markdown</c>.</param>
/// <param name="StreamKind">The stream subtype, such as <c>assistant</c> or <c>subagent-report</c>.</param>
/// <param name="Title">Optional title associated with the delta payload.</param>
public sealed record AgentStreamDeltaEvent(
    DateTimeOffset TimestampUtc,
    string AgentId,
    string AgentRole,
    string DeltaContent,
    string ContentFormat = "text",
    string StreamKind = "assistant",
    string? Title = null);
