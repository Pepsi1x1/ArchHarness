using ArchHarness.App.Agents;
using ArchHarness.App.Storage;
using ArchHarness.App.Workspace;

namespace ArchHarness.App.Core;

/// <summary>
/// Executes plan steps in dependency order, dispatching each step to the appropriate agent.
/// </summary>
public sealed class AgentStepExecutor : IAgentStepExecutor
{
    private readonly StepAgentDependencies _agents;
    private readonly IArtefactStore _artefactStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentStepExecutor"/> class.
    /// </summary>
    /// <param name="agents">Grouped agent references needed for step execution.</param>
    /// <param name="artefactStore">Store for persisting run events.</param>
    public AgentStepExecutor(
        StepAgentDependencies agents,
        IArtefactStore artefactStore)
    {
        this._agents = agents;
        this._artefactStore = artefactStore;
    }

    /// <summary>
    /// Executes all steps in the execution plan in dependency order, dispatching each to the
    /// correct agent and tracking completion.
    /// </summary>
    /// <param name="plan">The execution plan containing steps to run.</param>
    /// <param name="adapter">Workspace adapter for file and diff operations.</param>
    /// <param name="request">The originating run request.</param>
    /// <param name="runId">Unique identifier for the current run.</param>
    /// <param name="runDirectory">Directory where run artefacts are stored.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Token to signal cancellation.</param>
    /// <returns>The aggregated results of all step executions.</returns>
    public async Task<StepExecutionResult> ExecuteAsync(
        ExecutionPlan plan,
        IWorkspaceAdapter adapter,
        RunRequest request,
        string runId,
        string runDirectory,
        IProgress<RuntimeProgressEvent>? progress,
        CancellationToken cancellationToken)
    {
        string frontendPlan = string.Empty;
        IReadOnlyList<string> filesTouched = Array.Empty<string>();
        ArchitectureReview review = new ArchitectureReview(Array.Empty<ArchitectureFinding>(), Array.Empty<string>());
        SecurityReview securityReview = new SecurityReview(Array.Empty<SecurityFinding>(), Array.Empty<string>());

        Dictionary<string, Func<ExecutionPlanStep, Task>> agentStrategies = new Dictionary<string, Func<ExecutionPlanStep, Task>>
        {
            ["FrontendDeveloper"] = async (ExecutionPlanStep s) =>
            {
                IReadOnlyList<string> newFiles = await this._agents.FrontendDeveloper.ImplementAsync(
                    adapter,
                    s.Objective,
                    request.ModelOverrides,
                    this._agents.FrontendDeveloper.Id,
                    this._agents.FrontendDeveloper.Role,
                    cancellationToken);

                filesTouched = filesTouched
                    .Concat(newFiles)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                frontendPlan = newFiles.Count > 0
                    ? $"Frontend developer implemented and touched {newFiles.Count} file(s)."
                    : "Frontend developer step executed.";
            },
            ["BackendDeveloper"] = async (ExecutionPlanStep s) =>
            {
                IReadOnlyList<string> newFiles = await this._agents.BackendDeveloper.ImplementAsync(
                    adapter,
                    s.Objective,
                    request.ModelOverrides,
                    null,
                    this._agents.BackendDeveloper.Id,
                    this._agents.BackendDeveloper.Role,
                    cancellationToken);

                filesTouched = filesTouched
                    .Concat(newFiles)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            },
            ["Build"] = async (ExecutionPlanStep s) =>
            {
                await this._agents.Build.RunBuildAsync(
                    adapter,
                    s.Objective,
                    request.BuildCommand,
                    request.ModelOverrides,
                    this._agents.Build.Id,
                    this._agents.Build.Role,
                    cancellationToken);
            },
            ["CodingStyle"] = async (ExecutionPlanStep s) =>
            {
                string latestDiff = await adapter.DiffAsync(cancellationToken);
                await this._agents.CodingStyle.EnforceAsync(
                    new StyleEnforcementRequest(
                        DelegatedPrompt: s.Objective,
                        Diff: latestDiff,
                        WorkspaceRoot: adapter.RootPath,
                        FilesTouched: filesTouched,
                        LanguageScope: s.Languages,
                        ModelOverrides: request.ModelOverrides),
                    this._agents.CodingStyle.Id,
                    this._agents.CodingStyle.Role,
                    cancellationToken);
            },
            ["Security"] = async (ExecutionPlanStep s) =>
            {
                string latestDiff = await adapter.DiffAsync(cancellationToken);
                IReadOnlyList<string> securityFiles = request.ArchitectureLoopMode
                    ? ArchitectureLoopHelpers.EnumerateWorkspaceFiles(adapter.RootPath, s.Languages)
                    : filesTouched;
                string delegatedPrompt = request.ArchitectureLoopMode
                    ? ArchitectureLoopHelpers.BuildArchitectureLoopPrompt(s.Objective, request.ArchitectureLoopPrompt)
                    : s.Objective;
                securityReview = await this._agents.Security.ReviewAsync(
                    new SecurityReviewRequest(
                        DelegatedPrompt: delegatedPrompt,
                        Diff: latestDiff,
                        WorkspaceRoot: adapter.RootPath,
                        FilesTouched: securityFiles,
                        LanguageScope: s.Languages,
                        ModelOverrides: request.ModelOverrides),
                    this._agents.Security.Id,
                    this._agents.Security.Role,
                    cancellationToken);
            },
            ["Architecture"] = async (ExecutionPlanStep s) =>
            {
                string latestDiff = await adapter.DiffAsync(cancellationToken);
                IReadOnlyList<string> architectureFiles = request.ArchitectureLoopMode
                    ? ArchitectureLoopHelpers.EnumerateWorkspaceFiles(adapter.RootPath, s.Languages)
                    : filesTouched;
                string delegatedPrompt = request.ArchitectureLoopMode
                    ? ArchitectureLoopHelpers.BuildArchitectureLoopPrompt(s.Objective, request.ArchitectureLoopPrompt)
                    : s.Objective;
                review = await this._agents.Architecture.ReviewAsync(
                    new ArchitectureReviewRequest(
                        DelegatedPrompt: delegatedPrompt,
                        Diff: latestDiff,
                        WorkspaceRoot: adapter.RootPath,
                        FilesTouched: architectureFiles,
                        LanguageScope: s.Languages,
                        ModelOverrides: request.ModelOverrides),
                    this._agents.Architecture.Id,
                    this._agents.Architecture.Role,
                    cancellationToken);
            }
        };

        Dictionary<int, ExecutionPlanStep> pendingSteps = plan.Steps.ToDictionary(s => s.Id);
        HashSet<int> completedStepIds = new HashSet<int>();
        while (pendingSteps.Count > 0)
        {
            ExecutionPlanStep? step = pendingSteps.Values
                .OrderBy(s => s.Id)
                .FirstOrDefault(s => DependenciesSatisfied(s, completedStepIds, pendingSteps));

            if (step is null)
            {
                step = pendingSteps.Values.OrderBy(s => s.Id).First();
                await this._artefactStore.AppendEventAsync(runDirectory, new
                {
                    runId,
                    source = WellKnownSources.Orchestrator,
                    message = $"Dependency deadlock detected; force-executing step {step.Id}."
                }, cancellationToken);
            }

            await this._artefactStore.AppendEventAsync(runDirectory, new { runId, source = step.Agent, message = step.Objective }, cancellationToken);
            progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, step.Agent, "Delegated prompt started", step.Objective));
            if (!agentStrategies.TryGetValue(step.Agent, out Func<ExecutionPlanStep, Task>? strategy))
            {
                throw new InvalidOperationException($"Unrecognized agent role: '{step.Agent}'.");
            }

            try
            {
                await strategy(step);
            }
            catch (Exception ex) when (StructuredOutputParser.IsParseFailure(ex))
            {
                await this._artefactStore.AppendEventAsync(runDirectory, new
                {
                    runId,
                    source = step.Agent,
                    status = "failed",
                    failureType = "parse_error",
                    stepId = step.Id,
                    objective = step.Objective,
                    message = ex.Message
                }, cancellationToken);
                throw new InvalidOperationException(
                    $"Step {step.Id} ({step.Agent}) failed due to unparseable structured output. {ex.Message}",
                    ex);
            }
            catch (Exception ex)
            {
                await this._artefactStore.AppendEventAsync(runDirectory, new
                {
                    runId,
                    source = step.Agent,
                    status = "failed",
                    failureType = "execution_error",
                    stepId = step.Id,
                    objective = step.Objective,
                    message = ex.Message
                }, cancellationToken);
                throw;
            }

            completedStepIds.Add(step.Id);
            pendingSteps.Remove(step.Id);
        }

        return new StepExecutionResult(frontendPlan, filesTouched, review, securityReview);
    }

    private static bool DependenciesSatisfied(
        ExecutionPlanStep step,
        ISet<int> completedStepIds,
        IReadOnlyDictionary<int, ExecutionPlanStep> pendingSteps)
    {
        if (step.DependsOnStepIds is null || step.DependsOnStepIds.Count == 0)
        {
            return true;
        }

        foreach (int dep in step.DependsOnStepIds)
        {
            if (pendingSteps.ContainsKey(dep))
            {
                return false;
            }

            if (!completedStepIds.Contains(dep))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Contains the aggregated results from executing all plan steps.
    /// </summary>
    /// <param name="FrontendPlan">The frontend plan produced by the Frontend Developer agent.</param>
    /// <param name="FilesTouched">Files modified by the Backend Developer agent.</param>
    /// <param name="Review">The architecture review produced by the Architecture agent.</param>
    public sealed record StepExecutionResult(
        string FrontendPlan,
        IReadOnlyList<string> FilesTouched,
        ArchitectureReview Review,
        SecurityReview SecurityReview);

    /// <summary>
    /// Groups the agent references required for plan step execution, reducing constructor over-injection.
    /// </summary>
    public sealed class StepAgentDependencies
    {
        /// <summary>Gets the frontend developer agent.</summary>
        public FrontendDeveloperAgent FrontendDeveloper { get; }
        /// <summary>Gets the backend developer agent.</summary>
        public BackendDeveloperAgent BackendDeveloper { get; }
        /// <summary>Gets the build agent.</summary>
        public BuildAgent Build { get; }
        /// <summary>Gets the coding style agent.</summary>
        public CodingStyleAgent CodingStyle { get; }
        /// <summary>Gets the security agent.</summary>
        public SecurityAgent Security { get; }
        /// <summary>Gets the architecture agent.</summary>
        public ArchitectureAgent Architecture { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="StepAgentDependencies"/> class.
        /// </summary>
        /// <param name="frontendDeveloper">The frontend developer agent.</param>
        /// <param name="backendDeveloper">The backend developer agent.</param>
        /// <param name="build">The build agent.</param>
        /// <param name="codingStyle">The coding style agent.</param>
        /// <param name="security">The security agent.</param>
        /// <param name="architecture">The architecture agent.</param>
        public StepAgentDependencies(
            FrontendDeveloperAgent frontendDeveloper,
            BackendDeveloperAgent backendDeveloper,
            BuildAgent build,
            CodingStyleAgent codingStyle,
            SecurityAgent security,
            ArchitectureAgent architecture)
        {
            this.FrontendDeveloper = frontendDeveloper;
            this.BackendDeveloper = backendDeveloper;
            this.Build = build;
            this.CodingStyle = codingStyle;
            this.Security = security;
            this.Architecture = architecture;
        }
    }
}
