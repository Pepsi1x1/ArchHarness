using ArchHarness.App.Agents;
using ArchHarness.App.Constants;
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
    private readonly RuntimeStateAccessors _stateAccessors;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentStepExecutor"/> class.
    /// </summary>
    /// <param name="agents">Grouped agent references needed for step execution.</param>
    /// <param name="artefactStore">Store for persisting run events.</param>
    public AgentStepExecutor(
        StepAgentDependencies agents,
        IArtefactStore artefactStore,
        RuntimeStateAccessors stateAccessors)
    {
        this._agents = agents;
        this._artefactStore = artefactStore;
        this._stateAccessors = stateAccessors;
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
        ExecutionState state = new ExecutionState();
        Dictionary<string, Func<ExecutionPlanStep, Task>> agentStrategies = this.CreateAgentStrategies(adapter, request, state, cancellationToken);

        Dictionary<int, ExecutionPlanStep> pendingSteps = plan.Steps.ToDictionary(s => s.Id);
        HashSet<int> completedStepIds = new HashSet<int>();
        while (pendingSteps.Count > 0)
        {
            ExecutionPlanStep step = await this.ResolveNextStepAsync(pendingSteps, completedStepIds, runDirectory, runId, cancellationToken);
            await this.ExecuteStepAsync(step, agentStrategies, runDirectory, runId, progress, cancellationToken);

            completedStepIds.Add(step.Id);
            pendingSteps.Remove(step.Id);
        }

        return new StepExecutionResult(state.FrontendPlan, state.FilesTouched, state.Review, state.SecurityReview);
    }

    private Dictionary<string, Func<ExecutionPlanStep, Task>> CreateAgentStrategies(
        IWorkspaceAdapter adapter,
        RunRequest request,
        ExecutionState state,
        CancellationToken cancellationToken)
        => new Dictionary<string, Func<ExecutionPlanStep, Task>>
        {
            [AgentNames.FRONTEND_DEVELOPER] = step => this.ExecuteFrontendDeveloperStepAsync(step, adapter, request, state, cancellationToken),
            [AgentNames.BACKEND_DEVELOPER] = step => this.ExecuteBackendDeveloperStepAsync(step, adapter, request, state, cancellationToken),
            [AgentNames.BUILD] = step => this.ExecuteBuildStepAsync(step, adapter, request, cancellationToken),
            [AgentNames.CODING_STYLE] = step => this.ExecuteCodingStyleStepAsync(step, adapter, request, state, cancellationToken),
            [AgentNames.SECURITY] = step => this.ExecuteSecurityStepAsync(step, adapter, request, state, cancellationToken),
            [AgentNames.ARCHITECTURE] = step => this.ExecuteArchitectureStepAsync(step, adapter, request, state, cancellationToken)
        };

    private async Task<ExecutionPlanStep> ResolveNextStepAsync(
        IReadOnlyDictionary<int, ExecutionPlanStep> pendingSteps,
        ISet<int> completedStepIds,
        string runDirectory,
        string runId,
        CancellationToken cancellationToken)
    {
        ExecutionPlanStep? step = pendingSteps.Values
            .OrderBy(candidate => candidate.Id)
            .FirstOrDefault(candidate => DependenciesSatisfied(candidate, completedStepIds, pendingSteps));

        if (step is not null)
        {
            return step;
        }

        ExecutionPlanStep fallbackStep = pendingSteps.Values.OrderBy(candidate => candidate.Id).First();
        await this._artefactStore.AppendEventAsync(runDirectory, new
        {
            runId,
            source = WellKnownSources.ORCHESTRATOR,
            message = $"Dependency deadlock detected; force-executing step {fallbackStep.Id}."
        }, cancellationToken);
        return fallbackStep;
    }

    private async Task ExecuteStepAsync(
        ExecutionPlanStep step,
        IReadOnlyDictionary<string, Func<ExecutionPlanStep, Task>> agentStrategies,
        string runDirectory,
        string runId,
        IProgress<RuntimeProgressEvent>? progress,
        CancellationToken cancellationToken)
    {
        await this._artefactStore.AppendEventAsync(runDirectory, new { runId, source = step.Agent, message = step.Objective }, cancellationToken);
        progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, step.Agent, "Delegated prompt started", step.Objective));

        if (!agentStrategies.TryGetValue(step.Agent, out Func<ExecutionPlanStep, Task>? strategy))
        {
            throw new InvalidOperationException($"Unrecognized agent role: '{step.Agent}'.");
        }

        AgentExecutionContext? previousAgentContext = this._stateAccessors.AgentExecutionContext.Current;
        this._stateAccessors.AgentExecutionContext.SetCurrent(this.ResolveAgentExecutionContext(step.Agent));
        try
        {
            await strategy(step);
        }
        catch (Exception ex) when (StructuredOutputParser.IsParseFailure(ex))
        {
            await this.AppendStepFailureAsync(runDirectory, runId, step, "parse_error", ex.Message, cancellationToken);
            throw new InvalidOperationException(
                $"Step {step.Id} ({step.Agent}) failed due to unparseable structured output. {ex.Message}",
                ex);
        }
        catch (Exception ex)
        {
            await this.AppendStepFailureAsync(runDirectory, runId, step, "execution_error", ex.Message, cancellationToken);
            throw;
        }
        finally
        {
            this._stateAccessors.AgentExecutionContext.SetCurrent(previousAgentContext);
        }
    }

    private Task AppendStepFailureAsync(
        string runDirectory,
        string runId,
        ExecutionPlanStep step,
        string failureType,
        string message,
        CancellationToken cancellationToken)
        => this._artefactStore.AppendEventAsync(runDirectory, new
        {
            runId,
            source = step.Agent,
            status = "failed",
            failureType,
            stepId = step.Id,
            objective = step.Objective,
            message
        }, cancellationToken);

    private async Task ExecuteFrontendDeveloperStepAsync(ExecutionPlanStep step, IWorkspaceAdapter adapter, RunRequest request, ExecutionState state, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> newFiles = await this._agents.FrontendDeveloper.ImplementAsync(
            adapter,
            step.Objective,
            request.ModelOverrides,
            this._agents.FrontendDeveloper.Id,
            this._agents.FrontendDeveloper.Role,
            cancellationToken);

        state.FilesTouched = MergeFilesTouched(state.FilesTouched, newFiles);
        state.FrontendPlan = newFiles.Count > 0
            ? $"Frontend developer implemented and touched {newFiles.Count} file(s)."
            : "Frontend developer step executed.";
    }

    private async Task ExecuteBackendDeveloperStepAsync(ExecutionPlanStep step, IWorkspaceAdapter adapter, RunRequest request, ExecutionState state, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> newFiles = await this._agents.BackendDeveloper.ImplementAsync(
            adapter,
            step.Objective,
            request.ModelOverrides,
            null,
            this._agents.BackendDeveloper.Id,
            this._agents.BackendDeveloper.Role,
            cancellationToken);

        state.FilesTouched = MergeFilesTouched(state.FilesTouched, newFiles);
    }

    private Task ExecuteBuildStepAsync(ExecutionPlanStep step, IWorkspaceAdapter adapter, RunRequest request, CancellationToken cancellationToken)
        => this._agents.Build.RunBuildAsync(
            adapter,
            step.Objective,
            request.BuildCommand,
            request.ModelOverrides,
            this._agents.Build.Id,
            this._agents.Build.Role,
            cancellationToken);

    private async Task ExecuteCodingStyleStepAsync(ExecutionPlanStep step, IWorkspaceAdapter adapter, RunRequest request, ExecutionState state, CancellationToken cancellationToken)
    {
        string latestDiff = await adapter.DiffAsync(cancellationToken);
        await this._agents.CodingStyle.EnforceAsync(
            new StyleEnforcementRequest(
                DelegatedPrompt: BuildDelegatedPrompt(step.Objective, request),
                Diff: latestDiff,
                WorkspaceRoot: adapter.RootPath,
                FilesTouched: state.FilesTouched,
                LanguageScope: step.Languages,
                ModelOverrides: request.ModelOverrides),
            this._agents.CodingStyle.Id,
            this._agents.CodingStyle.Role,
            cancellationToken);
    }

    private async Task ExecuteSecurityStepAsync(ExecutionPlanStep step, IWorkspaceAdapter adapter, RunRequest request, ExecutionState state, CancellationToken cancellationToken)
    {
        string latestDiff = await adapter.DiffAsync(cancellationToken);
        state.SecurityReview = await this._agents.Security.ReviewAsync(
            new SecurityReviewRequest(
                DelegatedPrompt: BuildDelegatedPrompt(step.Objective, request),
                Diff: latestDiff,
                WorkspaceRoot: adapter.RootPath,
                FilesTouched: ResolveReviewFiles(adapter, request, state.FilesTouched, step.Languages),
                LanguageScope: step.Languages,
                ModelOverrides: request.ModelOverrides),
            this._agents.Security.Id,
            this._agents.Security.Role,
            cancellationToken);
    }

    private async Task ExecuteArchitectureStepAsync(ExecutionPlanStep step, IWorkspaceAdapter adapter, RunRequest request, ExecutionState state, CancellationToken cancellationToken)
    {
        string latestDiff = await adapter.DiffAsync(cancellationToken);
        state.Review = await this._agents.Architecture.ReviewAsync(
            new ArchitectureReviewRequest(
                DelegatedPrompt: BuildDelegatedPrompt(step.Objective, request),
                Diff: latestDiff,
                WorkspaceRoot: adapter.RootPath,
                FilesTouched: ResolveReviewFiles(adapter, request, state.FilesTouched, step.Languages),
                LanguageScope: step.Languages,
                ModelOverrides: request.ModelOverrides),
            this._agents.Architecture.Id,
            this._agents.Architecture.Role,
            cancellationToken);
    }

    private static IReadOnlyList<string> MergeFilesTouched(IReadOnlyList<string> existingFiles, IReadOnlyList<string> newFiles)
        => existingFiles
            .Concat(newFiles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string BuildDelegatedPrompt(string objective, RunRequest request)
        => request.ArchitectureLoopMode
            ? ArchitectureLoopHelpers.BuildArchitectureLoopPrompt(objective, request.ArchitectureLoopPrompt)
            : objective;

    private static IReadOnlyList<string> ResolveReviewFiles(IWorkspaceAdapter adapter, RunRequest request, IReadOnlyList<string> filesTouched, IReadOnlyList<string>? languageScope)
        => request.ArchitectureLoopMode
            ? ArchitectureLoopHelpers.EnumerateWorkspaceFiles(adapter.RootPath, languageScope)
            : filesTouched;

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

    private AgentExecutionContext ResolveAgentExecutionContext(string stepAgent)
        => stepAgent switch
        {
            AgentNames.FRONTEND_DEVELOPER => new AgentExecutionContext(this._agents.FrontendDeveloper.Id, this._agents.FrontendDeveloper.Role),
            AgentNames.BACKEND_DEVELOPER => new AgentExecutionContext(this._agents.BackendDeveloper.Id, this._agents.BackendDeveloper.Role),
            AgentNames.BUILD => new AgentExecutionContext(this._agents.Build.Id, this._agents.Build.Role),
            AgentNames.CODING_STYLE => new AgentExecutionContext(this._agents.CodingStyle.Id, this._agents.CodingStyle.Role),
            AgentNames.SECURITY => new AgentExecutionContext(this._agents.Security.Id, this._agents.Security.Role),
            AgentNames.ARCHITECTURE => new AgentExecutionContext(this._agents.Architecture.Id, this._agents.Architecture.Role),
            _ => new AgentExecutionContext(stepAgent, stepAgent)
        };

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

    private sealed class ExecutionState
    {
        public string FrontendPlan { get; set; } = string.Empty;

        public IReadOnlyList<string> FilesTouched { get; set; } = Array.Empty<string>();

        public ArchitectureReview Review { get; set; } = new ArchitectureReview(Array.Empty<ArchitectureFinding>(), Array.Empty<string>());

        public SecurityReview SecurityReview { get; set; } = new SecurityReview(Array.Empty<SecurityFinding>(), Array.Empty<string>());
    }

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
