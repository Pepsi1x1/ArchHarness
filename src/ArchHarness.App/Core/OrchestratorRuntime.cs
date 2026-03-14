using ArchHarness.App.Agents;
using ArchHarness.App.Copilot;
using ArchHarness.App.Workspace;

namespace ArchHarness.App.Core;

/// <summary>
/// Orchestrates a full run by coordinating plan execution, architecture review, build validation,
/// and artifact persistence through dedicated collaborator classes.
/// </summary>
public sealed class OrchestratorRuntime
{
    private const string ORCHESTRATOR_SOURCE = "orchestrator";

    private readonly OrchestratorAgentDependencies _agentDependencies;
    private readonly ICopilotClient _copilotClient;
    private readonly RunInfrastructure _runInfrastructure;
    private readonly IArchitectureReviewLoop _architectureReviewLoop;
    private readonly IPlanExecutor _planExecutor;
    private readonly IBuildValidator _buildValidator;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrchestratorRuntime"/> class.
    /// </summary>
    public OrchestratorRuntime(
        OrchestratorAgentDependencies agentDependencies,
        ICopilotClient copilotClient,
        RunInfrastructure runInfrastructure,
        IArchitectureReviewLoop architectureReviewLoop,
        IPlanExecutor planExecutor,
        IBuildValidator buildValidator)
    {
        this._agentDependencies = agentDependencies;
        this._copilotClient = copilotClient;
        this._runInfrastructure = runInfrastructure;
        this._architectureReviewLoop = architectureReviewLoop;
        this._planExecutor = planExecutor;
        this._buildValidator = buildValidator;
    }

    /// <summary>
    /// Executes a full orchestrated run: workspace initialization, plan execution, architecture review,
    /// build validation, and artifact persistence.
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
        this._runInfrastructure.RunContextAccessor.SetCurrent(new RunContext(runId, runDirectory));
        using CancellationTokenSource sessionEventCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task sessionEventPump = Task.Run(async () => await this._runInfrastructure.EventLogger.PumpSessionEventsAsync(runDirectory, runId, sessionEventCts.Token), CancellationToken.None);

        try
        {
            await this._runInfrastructure.EventLogger.AppendEventAsync(runDirectory, new { runId, source = ORCHESTRATOR_SOURCE, message = "Run started" }, cancellationToken);
            await this._runInfrastructure.EventLogger.AppendEventAsync(runDirectory, new
            {
                runId,
                source = "request",
                message = "Run request received",
                taskPrompt = request.TaskPrompt,
                workspacePath = request.WorkspacePath,
                workspaceMode = request.WorkspaceMode,
                workflow = request.Workflow,
                projectName = request.ProjectName,
                buildCommand = request.BuildCommand,
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
            progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, ORCHESTRATOR_SOURCE, "Run started"));

            PlanExecutionResult planResult;
            try
            {
                planResult = await this._planExecutor.BuildAndExecuteAsync(
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
                    source = ORCHESTRATOR_SOURCE,
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
            (review, securityReview, filesTouched) = await this._architectureReviewLoop.RunAsync(
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

            BuildValidationResult validation = await this._buildValidator.ExecuteAndValidateAsync(
                plan,
                review,
                securityReview,
                adapter,
                request,
                runId,
                runDirectory,
                progress,
                cancellationToken);

            string summary = $"""
                # Final Summary
                - Completed: {validation.Completed}
                - FrontendPlan: {frontendPlan}
                - FilesTouched: {string.Join(", ", filesTouched)}
                - SecurityHighFindings: {securityReview.Findings.Count(f => string.Equals(f.Severity, "high", StringComparison.OrdinalIgnoreCase))}
                - ArchitectureHighFindings: {review.Findings.Count(f => string.Equals(f.Severity, "high", StringComparison.OrdinalIgnoreCase))}
                - BuildExecuted: {validation.BuildResult.Executed}
                - BuildPassed: {validation.BuildResult.Passed}
                """;
            await this._runInfrastructure.ArtifactWriter.WriteFinalSummaryAsync(runDirectory, summary, cancellationToken);

            string[] modelOverrides = request.ModelOverrides?.Select(pair => $"{pair.Key}={pair.Value}").ToArray() ?? Array.Empty<string>();
            IReadOnlyList<CopilotModelUsage> usage = this._copilotClient.GetUsageSnapshot();

            await this._runInfrastructure.ArtifactWriter.WriteRunLogAsync(runDirectory, new
            {
                status = validation.Completed ? "completed" : "incomplete",
                request.WorkspaceMode,
                request.Workflow,
                modelOverrides,
                agents = new[]
                {
                    new { role = "orchestration", model = this._agentDependencies.OrchestrationAgent.ResolveModel(request.ModelOverrides) },
                    new { role = "frontend-developer", model = this._agentDependencies.FrontendDeveloperAgent.ResolveModel(request.ModelOverrides) },
                    new { role = "backend-developer", model = this._agentDependencies.BackendDeveloperAgent.ResolveModel(request.ModelOverrides) },
                    new { role = "build", model = this._agentDependencies.BuildAgent.ResolveModel(request.ModelOverrides) },
                    new { role = "coding-style", model = this._agentDependencies.CodingStyleAgent.ResolveModel(request.ModelOverrides) },
                    new { role = "security", model = this._agentDependencies.SecurityAgent.ResolveModel(request.ModelOverrides) },
                    new { role = "architecture", model = this._agentDependencies.ArchitectureAgent.ResolveModel(request.ModelOverrides) }
                },
                copilotUsage = usage
            }, cancellationToken);

            await this._runInfrastructure.EventLogger.AppendEventAsync(runDirectory, new { runId, source = ORCHESTRATOR_SOURCE, message = "Run completed" }, cancellationToken);
            progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, ORCHESTRATOR_SOURCE, "Run completed"));

            await sessionEventCts.CancelAsync();
            await sessionEventPump;
            return new RunArtefacts(runId, runDirectory);
        }
        finally
        {
            this._runInfrastructure.RunContextAccessor.SetCurrent(null);
        }
    }

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
}
