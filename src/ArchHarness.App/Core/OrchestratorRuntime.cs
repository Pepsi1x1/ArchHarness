using ArchHarness.App.Agents;
using ArchHarness.App.Copilot;
using ArchHarness.App.Workspace;

namespace ArchHarness.App.Core;

/// <summary>
/// Orchestrates a full run by coordinating plan execution, architecture review,
/// and artifact persistence through dedicated collaborator classes.
/// </summary>
public sealed class OrchestratorRuntime
{
    private readonly AgentsOptions _agentsOptions;
    private readonly OrchestratorAgentDependencies _agentDependencies;
    private readonly ICopilotClient _copilotClient;
    private readonly RunInfrastructure _runInfrastructure;
    private readonly RunPhaseDependencies _runPhases;
    private readonly RuntimeStateAccessors _stateAccessors;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrchestratorRuntime"/> class.
    /// </summary>
    public OrchestratorRuntime(
        OrchestratorAgentDependencies agentDependencies,
        ICopilotClient copilotClient,
        Microsoft.Extensions.Options.IOptions<AgentsOptions> agentsOptions,
        RunInfrastructure runInfrastructure,
        RunPhaseDependencies runPhases,
        RuntimeStateAccessors stateAccessors)
    {
        this._agentsOptions = agentsOptions.Value;
        this._agentDependencies = agentDependencies;
        this._copilotClient = copilotClient;
        this._runInfrastructure = runInfrastructure;
        this._runPhases = runPhases;
        this._stateAccessors = stateAccessors;
    }

    /// <summary>
    /// Executes a full orchestrated run: workspace initialization, plan execution, architecture review,
    /// completion validation, and artifact persistence.
    /// </summary>
    public async Task<RunArtefacts> RunAsync(
        RunRequest request,
        IProgress<RuntimeProgressEvent>? progress = null,
        CancellationToken cancellationToken = default)
    {
        IWorkspaceAdapter adapter = WorkspaceAdapterFactory.Create(request.WorkspaceMode, request.WorkspacePath);
        bool initGit = request.WorkspaceMode is "new-project" or "existing-git";
        await adapter.InitializeAsync(request.WorkspaceMode == "new-project" ? request.ProjectName : null, initGit, cancellationToken);

        BuildCommandSelection initialBuildSelection = BuildCommandInference.Select(
            adapter.RootPath,
            request.BuildCommand,
            request.WorkspaceMode,
            request.ProjectName);
        if (!string.Equals(initialBuildSelection.Command, request.BuildCommand, StringComparison.Ordinal))
        {
            request = request with { BuildCommand = initialBuildSelection.Command };
        }

        string runDirectory = this._runInfrastructure.ArtifactWriter.CreateRunDirectory(adapter.RootPath);
        string runId = Path.GetFileName(runDirectory);
        this._stateAccessors.PermissionHandlerMode.SetCurrent(PermissionHandlerModes.Normalize(request.PermissionHandlerMode));
        ReviewLoopAgentSelection reviewLoopAgents = request.ReviewLoopAgents ?? this._agentsOptions.GetReviewLoopAgentSelection();
        this._stateAccessors.ReviewLoopAgentSelection.SetCurrent(reviewLoopAgents);
        this._stateAccessors.WorkspaceRoot.SetCurrent(adapter.RootPath);
        this._runInfrastructure.RunContextAccessor.SetCurrent(new RunContext(runId, runDirectory));
        using CancellationTokenSource sessionEventCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task sessionEventPump = Task.Run(async () => await this._runInfrastructure.EventLogger.PumpSessionEventsAsync(runDirectory, runId, sessionEventCts.Token), CancellationToken.None);

        try
        {
            await this._runInfrastructure.EventLogger.AppendEventAsync(runDirectory, new { runId, source = WellKnownSources.Orchestrator, message = "Run started" }, cancellationToken);
            await this._runInfrastructure.EventLogger.AppendEventAsync(runDirectory, new
            {
                runId,
                source = "request",
                message = "Run request received",
                projectId = request.ProjectId,
                taskPrompt = request.TaskPrompt,
                runTitle = request.RunTitle,
                workspacePath = request.WorkspacePath,
                workspaceMode = request.WorkspaceMode,
                workflow = request.Workflow,
                projectName = request.ProjectName,
                buildCommand = request.BuildCommand,
                permissionHandlerMode = request.PermissionHandlerMode,
                reviewLoopAgents,
                modelOverrides = request.ModelOverrides
            }, cancellationToken);
            await this._runInfrastructure.EventLogger.AppendEventAsync(runDirectory, new
            {
                runId,
                source = "build-selection",
                message = "Initial build command selection",
                buildCommand = request.BuildCommand,
                inferred = initialBuildSelection.Inferred,
                reason = initialBuildSelection.Reason
            }, cancellationToken);
            progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, WellKnownSources.Orchestrator, "Run started"));

            PlanExecutionResult planResult;
            try
            {
                planResult = await this._runPhases.PlanExecutor.BuildAndExecuteAsync(
                    request,
                    adapter,
                    runId,
                    runDirectory,
                    progress,
                    cancellationToken);
            }
            catch (Exception ex) when (StructuredOutputParser.IsParseFailure(ex))
            {
                await this._runInfrastructure.EventLogger.AppendEventAsync(runDirectory, new
                {
                    runId,
                    source = WellKnownSources.Orchestrator,
                    status = "failed",
                    failureType = "parse_error",
                    stage = "planning",
                    message = ex.Message
                }, cancellationToken);
                throw;
            }

            ExecutionPlan plan = planResult.Plan;
            string frontendPlan = planResult.StepResult.FrontendPlan;
            IReadOnlyList<string> filesTouched = planResult.StepResult.FilesTouched;
            ArchitectureReview review = planResult.StepResult.Review;
            SecurityReview securityReview = planResult.StepResult.SecurityReview;

            IReadOnlyList<string>? architectureLanguages = plan.Steps.LastOrDefault(s => s.Agent == "Architecture")?.Languages;
            IReadOnlyList<string>? securityLanguages = plan.Steps.LastOrDefault(s => s.Agent == "Security")?.Languages;
            (review, securityReview, filesTouched) = await this._runPhases.ArchitectureReviewLoop.RunAsync(
                new ArchitectureLoopRequest(
                    IterationStrategy: plan.IterationStrategy,
                    InitialReview: review,
                    InitialSecurityReview: securityReview,
                    FilesTouched: filesTouched,
                    ArchitectureLanguages: architectureLanguages,
                    SecurityLanguages: securityLanguages,
                    RunRequest: request),
                adapter,
                progress,
                cancellationToken);

            if (review.RequiredActions.Contains(ArchitectureReviewLoop.NO_PROGRESS_BLOCKED_STATUS, StringComparer.OrdinalIgnoreCase)
                || securityReview.RequiredActions.Contains(ArchitectureReviewLoop.NO_PROGRESS_BLOCKED_STATUS, StringComparer.OrdinalIgnoreCase))
            {
                await this._runInfrastructure.EventLogger.AppendEventAsync(runDirectory, new
                {
                    runId,
                    source = "architecture-loop",
                    status = "blocked",
                    message = "Architecture review iterations produced identical findings; loop stopped early."
                }, cancellationToken);
            }

            await this._runInfrastructure.ArtifactWriter.WriteArchitectureReviewAsync(runDirectory, review, cancellationToken);
            await this._runInfrastructure.ArtifactWriter.WriteSecurityReviewAsync(runDirectory, securityReview, cancellationToken);

            bool completed = await this._agentDependencies.OrchestrationAgent.ValidateCompletionAsync(
                new CompletionValidationRequest(
                    Plan: plan,
                    Review: review,
                    SecurityReview: securityReview,
                    ModelOverrides: request.ModelOverrides),
                this._agentDependencies.OrchestrationAgent.Id,
                this._agentDependencies.OrchestrationAgent.Role,
                cancellationToken);

            string summary = $"""
                # Final Summary
                - Completed: {completed}
                - FrontendPlan: {frontendPlan}
                - FilesTouched: {string.Join(", ", filesTouched)}
                - SecurityHighFindings: {securityReview.Findings.Count(f => string.Equals(f.Severity, "high", StringComparison.OrdinalIgnoreCase))}
                - ArchitectureHighFindings: {review.Findings.Count(f => string.Equals(f.Severity, "high", StringComparison.OrdinalIgnoreCase))}
                """;
            await this._runInfrastructure.ArtifactWriter.WriteFinalSummaryAsync(runDirectory, summary, cancellationToken);

            string[] modelOverrides = request.ModelOverrides?.Select(pair => $"{pair.Key}={pair.Value}").ToArray() ?? Array.Empty<string>();
            IReadOnlyList<CopilotModelUsage> usage = this._copilotClient.GetUsageSnapshot();
            object[] agentModelUsage = BuildAgentModelUsage(this._agentDependencies, request.ModelOverrides);

            await this._runInfrastructure.ArtifactWriter.WriteRunLogAsync(runDirectory, new
            {
                status = completed ? "completed" : "incomplete",
                projectId = request.ProjectId,
                projectName = request.ProjectName,
                runTitle = request.RunTitle,
                request.WorkspaceMode,
                request.Workflow,
                request.PermissionHandlerMode,
                modelOverrides,
                agents = agentModelUsage,
                copilotUsage = usage
            }, cancellationToken);

            await this._runInfrastructure.EventLogger.AppendEventAsync(runDirectory, new { runId, source = WellKnownSources.Orchestrator, message = "Run completed" }, cancellationToken);
            progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, WellKnownSources.Orchestrator, "Run completed"));

            await sessionEventCts.CancelAsync();
            await sessionEventPump;
            return new RunArtefacts(runId, runDirectory);
        }
        finally
        {
            this._stateAccessors.PermissionHandlerMode.SetCurrent(null);
            this._stateAccessors.ReviewLoopAgentSelection.SetCurrent(null);
            this._runInfrastructure.RunContextAccessor.SetCurrent(null);
            this._stateAccessors.WorkspaceRoot.SetCurrent(null);
        }
    }

    private static object[] BuildAgentModelUsage(OrchestratorAgentDependencies agents, IDictionary<string, string>? overrides)
        => new object[]
        {
            new { role = "orchestration", model = agents.OrchestrationAgent.ResolveModel(overrides) },
            new { role = "frontend-developer", model = agents.FrontendDeveloperAgent.ResolveModel(overrides) },
            new { role = "backend-developer", model = agents.BackendDeveloperAgent.ResolveModel(overrides) },
            new { role = "build", model = agents.BuildAgent.ResolveModel(overrides) },
            new { role = "coding-style", model = agents.CodingStyleAgent.ResolveModel(overrides) },
            new { role = "security", model = agents.SecurityAgent.ResolveModel(overrides) },
            new { role = "architecture", model = agents.ArchitectureAgent.ResolveModel(overrides) }
        };

    /// <summary>
    /// Groups agent references needed by the orchestrator for model resolution in run logs.
    /// </summary>
    public sealed class OrchestratorAgentDependencies
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="OrchestratorAgentDependencies"/> class.
        /// </summary>
        public OrchestratorAgentDependencies(
            OrchestrationAgent orchestrationAgent,
            FrontendDeveloperAgent frontendDeveloperAgent,
            BackendDeveloperAgent backendDeveloperAgent,
            BuildAgent buildAgent,
            CodingStyleAgent codingStyleAgent,
            SecurityAgent securityAgent,
            ArchitectureAgent architectureAgent)
        {
            this.OrchestrationAgent = orchestrationAgent;
            this.FrontendDeveloperAgent = frontendDeveloperAgent;
            this.BackendDeveloperAgent = backendDeveloperAgent;
            this.BuildAgent = buildAgent;
            this.CodingStyleAgent = codingStyleAgent;
            this.SecurityAgent = securityAgent;
            this.ArchitectureAgent = architectureAgent;
        }

        /// <summary>
        /// Gets the orchestration agent used for planning and validation.
        /// </summary>
        public OrchestrationAgent OrchestrationAgent { get; }

        /// <summary>
        /// Gets the frontend developer agent used for UI/UX implementation.
        /// </summary>
        public FrontendDeveloperAgent FrontendDeveloperAgent { get; }

        /// <summary>
        /// Gets the backend developer agent used for code implementation.
        /// </summary>
        public BackendDeveloperAgent BackendDeveloperAgent { get; }

        /// <summary>
        /// Gets the build agent used for delegated build execution.
        /// </summary>
        public BuildAgent BuildAgent { get; }

        /// <summary>
        /// Gets the coding style agent used for style enforcement.
        /// </summary>
        public CodingStyleAgent CodingStyleAgent { get; }

        /// <summary>
        /// Gets the security agent used for security review and enforcement.
        /// </summary>
        public SecurityAgent SecurityAgent { get; }

        /// <summary>
        /// Gets the architecture agent used for architecture review and enforcement.
        /// </summary>
        public ArchitectureAgent ArchitectureAgent { get; }
    }

    /// <summary>
    /// Groups the run-phase collaborators (plan execution and architecture review)
    /// to reduce constructor over-injection in <see cref="OrchestratorRuntime"/>.
    /// </summary>
    public sealed class RunPhaseDependencies
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RunPhaseDependencies"/> class.
        /// </summary>
        /// <param name="architectureReviewLoop">The architecture review iteration loop.</param>
        /// <param name="planExecutor">The execution plan builder and dispatcher.</param>
        public RunPhaseDependencies(
            IArchitectureReviewLoop architectureReviewLoop,
            IPlanExecutor planExecutor)
        {
            this.ArchitectureReviewLoop = architectureReviewLoop;
            this.PlanExecutor = planExecutor;
        }

        /// <summary>Gets the architecture review iteration loop.</summary>
        public IArchitectureReviewLoop ArchitectureReviewLoop { get; }

        /// <summary>Gets the execution plan builder and dispatcher.</summary>
        public IPlanExecutor PlanExecutor { get; }
    }
}
