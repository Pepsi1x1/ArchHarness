using ArchHarness.App.Agents;
using ArchHarness.App.Constants;
using ArchHarness.App.Workspace;

namespace ArchHarness.App.Core;

/// <summary>
/// Dispatches execution-plan steps to the correct agent implementation.
/// </summary>
public interface IAgentStepDispatcher
{
    /// <summary>
    /// Executes a single step against the supplied workspace and returns an immutable outcome.
    /// </summary>
    Task<StepOutcome> ExecuteAsync(
        ExecutionPlanStep step,
        IWorkspaceAdapter adapter,
        RunRequest request,
        IReadOnlyList<string> accumulatedFilesTouched,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resolves the execution context metadata for a step's agent role.
    /// </summary>
    AgentExecutionContext ResolveAgentExecutionContext(string stepAgent);
}

/// <summary>
/// Default implementation of <see cref="IAgentStepDispatcher"/>.
/// </summary>
public sealed class AgentStepDispatcher : IAgentStepDispatcher
{
    private readonly FrontendDeveloperAgent _frontendDeveloper;
    private readonly BackendDeveloperAgent _backendDeveloper;
    private readonly BuildAgent _build;
    private readonly AgentStepReviewDispatcher _reviewDispatcher;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentStepDispatcher"/> class.
    /// </summary>
    public AgentStepDispatcher(
        FrontendDeveloperAgent frontendDeveloper,
        BackendDeveloperAgent backendDeveloper,
        BuildAgent build,
        AgentStepReviewDispatcher reviewDispatcher)
    {
        this._frontendDeveloper = frontendDeveloper;
        this._backendDeveloper = backendDeveloper;
        this._build = build;
        this._reviewDispatcher = reviewDispatcher;
    }

    /// <inheritdoc />
    public async Task<StepOutcome> ExecuteAsync(
        ExecutionPlanStep step,
        IWorkspaceAdapter adapter,
        RunRequest request,
        IReadOnlyList<string> accumulatedFilesTouched,
        CancellationToken cancellationToken)
    {
        switch (step.Agent)
        {
            case AgentNames.FRONTEND_DEVELOPER:
            {
                IReadOnlyList<string> newFiles = await this._frontendDeveloper.ImplementAsync(
                    adapter,
                    step.Objective,
                    request.ModelOverrides,
                    this._frontendDeveloper.Id,
                    this._frontendDeveloper.Role,
                    cancellationToken).ConfigureAwait(false);

                string frontendPlan = newFiles.Count > 0
                    ? $"Frontend developer implemented and touched {newFiles.Count} file(s)."
                    : "Frontend developer step executed.";
                return new StepOutcome(
                    step.Id,
                    step.Agent,
                    newFiles,
                    FrontendPlanDelta: frontendPlan,
                    CompletionStatus: StepCompletionStatuses.COMPLETE);
            }

            case AgentNames.BACKEND_DEVELOPER:
            {
                IReadOnlyList<string> newFiles = await this._backendDeveloper.ImplementAsync(
                    adapter,
                    step.Objective,
                    request.ModelOverrides,
                    null,
                    this._backendDeveloper.Id,
                    this._backendDeveloper.Role,
                    cancellationToken).ConfigureAwait(false);

                return new StepOutcome(
                    step.Id,
                    step.Agent,
                    newFiles,
                    CompletionStatus: StepCompletionStatuses.COMPLETE);
            }

            case AgentNames.BUILD:
                BuildOutcome buildOutcome = await this._build.RunBuildAsync(
                    adapter,
                    step.Objective,
                    request.BuildCommand,
                    request.ModelOverrides,
                    step.Id,
                    this._build.Id,
                    this._build.Role,
                    cancellationToken).ConfigureAwait(false);
                return new StepOutcome(step.Id, step.Agent, Array.Empty<string>(), BuildOutcome: buildOutcome);

            default:
                return await this._reviewDispatcher.ExecuteAsync(step, adapter, request, accumulatedFilesTouched, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public AgentExecutionContext ResolveAgentExecutionContext(string stepAgent)
        => stepAgent switch
        {
            AgentNames.FRONTEND_DEVELOPER => new AgentExecutionContext(this._frontendDeveloper.Id, this._frontendDeveloper.Role),
            AgentNames.BACKEND_DEVELOPER => new AgentExecutionContext(this._backendDeveloper.Id, this._backendDeveloper.Role),
            AgentNames.BUILD => new AgentExecutionContext(this._build.Id, this._build.Role),
            _ => this._reviewDispatcher.ResolveAgentExecutionContext(stepAgent)
        };
}

/// <summary>
/// Dispatches style, security, and architecture review steps.
/// </summary>
public sealed class AgentStepReviewDispatcher
{
    private readonly CodingStyleAgent _codingStyle;
    private readonly SecurityAgent _security;
    private readonly ArchitectureAgent _architecture;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentStepReviewDispatcher"/> class.
    /// </summary>
    public AgentStepReviewDispatcher(
        CodingStyleAgent codingStyle,
        SecurityAgent security,
        ArchitectureAgent architecture)
    {
        this._codingStyle = codingStyle;
        this._security = security;
        this._architecture = architecture;
    }

    /// <summary>
    /// Executes a review-oriented plan step and returns an immutable outcome.
    /// </summary>
    public async Task<StepOutcome> ExecuteAsync(
        ExecutionPlanStep step,
        IWorkspaceAdapter adapter,
        RunRequest request,
        IReadOnlyList<string> accumulatedFilesTouched,
        CancellationToken cancellationToken)
    {
        switch (step.Agent)
        {
            case AgentNames.CODING_STYLE:
            {
                string latestDiff = await adapter.DiffAsync(cancellationToken).ConfigureAwait(false);
                await this._codingStyle.EnforceAsync(
                    new StyleEnforcementRequest(
                        AgentStepExecutor.BuildDelegatedPrompt(step.Objective, request),
                        latestDiff,
                        adapter.RootPath,
                        accumulatedFilesTouched,
                        step.Languages,
                        request.ModelOverrides),
                    this._codingStyle.Id,
                    this._codingStyle.Role,
                    cancellationToken).ConfigureAwait(false);
                return new StepOutcome(step.Id, step.Agent, Array.Empty<string>());
            }

            case AgentNames.SECURITY:
            {
                string latestDiff = await adapter.DiffAsync(cancellationToken).ConfigureAwait(false);
                SecurityReview securityReview = await this._security.ReviewAsync(
                    new SecurityReviewRequest(
                        AgentStepExecutor.BuildDelegatedPrompt(step.Objective, request),
                        latestDiff,
                        adapter.RootPath,
                        AgentStepExecutor.ResolveReviewFiles(adapter, request, accumulatedFilesTouched, step.Languages),
                        step.Languages,
                        request.ModelOverrides),
                    this._security.Id,
                    this._security.Role,
                    cancellationToken).ConfigureAwait(false);
                return new StepOutcome(step.Id, step.Agent, Array.Empty<string>(), SecurityReview: securityReview);
            }

            case AgentNames.ARCHITECTURE:
            {
                string latestDiff = await adapter.DiffAsync(cancellationToken).ConfigureAwait(false);
                ArchitectureReview review = await this._architecture.ReviewAsync(
                    new ArchitectureReviewRequest(
                        AgentStepExecutor.BuildDelegatedPrompt(step.Objective, request),
                        latestDiff,
                        adapter.RootPath,
                        AgentStepExecutor.ResolveReviewFiles(adapter, request, accumulatedFilesTouched, step.Languages),
                        step.Languages,
                        request.ModelOverrides),
                    this._architecture.Id,
                    this._architecture.Role,
                    cancellationToken).ConfigureAwait(false);
                return new StepOutcome(step.Id, step.Agent, Array.Empty<string>(), Review: review);
            }

            default:
                throw new InvalidOperationException($"Unrecognized agent role: '{step.Agent}'.");
        }
    }

    /// <summary>
    /// Resolves the execution context metadata for a review-oriented agent role.
    /// </summary>
    public AgentExecutionContext ResolveAgentExecutionContext(string stepAgent)
        => stepAgent switch
        {
            AgentNames.CODING_STYLE => new AgentExecutionContext(this._codingStyle.Id, this._codingStyle.Role),
            AgentNames.SECURITY => new AgentExecutionContext(this._security.Id, this._security.Role),
            AgentNames.ARCHITECTURE => new AgentExecutionContext(this._architecture.Id, this._architecture.Role),
            _ => new AgentExecutionContext(stepAgent, stepAgent)
        };
}
