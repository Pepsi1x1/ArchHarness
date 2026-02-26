using ArchHarness.App.Agents;
using ArchHarness.App.Copilot;
using ArchHarness.App.Storage;
using ArchHarness.App.Workspace;

namespace ArchHarness.App.Core;

/// <summary>
/// Orchestrates a full run lifecycle including planning, agent execution, review loops, and build validation.
/// </summary>
public sealed class OrchestratorRuntime
{
    private const string ORCHESTRATOR_SOURCE = "orchestrator";
    private readonly OrchestrationAgent _orchestrationAgent;
    private readonly FrontendAgent _frontendAgent;
    private readonly BuilderAgent _builderAgent;
    private readonly StyleAgent _styleAgent;
    private readonly ArchitectureAgent _architectureAgent;
    private readonly IRunStore _runStore;
    private readonly IArtefactStore _artefactStore;
    private readonly ICopilotClient _copilotClient;
    private readonly IBuildRunner _buildRunner;
    private readonly IRunContextAccessor _runContextAccessor;
    private readonly ArchitectureReviewLoop _architectureReviewLoop;
    private readonly AgentStepExecutor _agentStepExecutor;
    private readonly SessionEventPump _sessionEventPump;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrchestratorRuntime"/> class.
    /// </summary>
    /// <param name="agentDependencies">Grouped agent dependencies.</param>
    /// <param name="serviceDependencies">Grouped service dependencies.</param>
    /// <param name="architectureReviewLoop">Loop for iterative architecture review remediation.</param>
    /// <param name="agentStepExecutor">Executes individual plan steps against agents.</param>
    /// <param name="sessionEventPump">Pump for forwarding Copilot session lifecycle events to storage.</param>
    public OrchestratorRuntime(
        OrchestratorAgentDependencies agentDependencies,
        OrchestratorServiceDependencies serviceDependencies,
        ArchitectureReviewLoop architectureReviewLoop,
        AgentStepExecutor agentStepExecutor,
        SessionEventPump sessionEventPump)
    {
        this._orchestrationAgent = agentDependencies.OrchestrationAgent;
        this._frontendAgent = agentDependencies.FrontendAgent;
        this._builderAgent = agentDependencies.BuilderAgent;
        this._styleAgent = agentDependencies.StyleAgent;
        this._architectureAgent = agentDependencies.ArchitectureAgent;
        this._runStore = serviceDependencies.RunStore;
        this._artefactStore = serviceDependencies.ArtefactStore;
        this._copilotClient = serviceDependencies.CopilotClient;
        this._buildRunner = serviceDependencies.BuildRunner;
        this._runContextAccessor = serviceDependencies.RunContextAccessor;
        this._architectureReviewLoop = architectureReviewLoop;
        this._agentStepExecutor = agentStepExecutor;
        this._sessionEventPump = sessionEventPump;
    }

    /// <summary>
    /// Executes a complete orchestration run including planning, building, style enforcement,
    /// architecture review, and build validation.
    /// </summary>
    /// <param name="request">The run request containing task prompt, workspace, and configuration.</param>
    /// <param name="progress">Optional progress reporter for tracking run stages.</param>
    /// <param name="cancellationToken">Token to signal cancellation.</param>
    /// <returns>The run artefacts containing the run identifier and output directory.</returns>
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

        string runDirectory = this._runStore.CreateRunDirectory(adapter.RootPath);
        string runId = Path.GetFileName(runDirectory);
        this._runContextAccessor.SetCurrent(new RunContext(runId, runDirectory));
        using CancellationTokenSource sessionEventCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task sessionEventPump = Task.Run(async () => await this._sessionEventPump.PumpSessionEventsAsync(runDirectory, runId, sessionEventCts.Token), CancellationToken.None);

        try
        {
            await this._artefactStore.AppendEventAsync(runDirectory, new { runId, source = ORCHESTRATOR_SOURCE, message = "Run started" }, cancellationToken);
            await this._artefactStore.AppendEventAsync(runDirectory, new
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
            await this._artefactStore.AppendEventAsync(runDirectory, new
            {
                runId,
                source = "build-selection",
                message = "Initial build command selection",
                buildCommand = request.BuildCommand,
                inferred = initialBuildSelection.Inferred,
                reason = initialBuildSelection.Reason
            }, cancellationToken);
            progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, ORCHESTRATOR_SOURCE, "Run started"));
            ExecutionPlan plan;
            try
            {
                plan = await this._orchestrationAgent.BuildExecutionPlanAsync(
                    request,
                    adapter.RootPath,
                    this._orchestrationAgent.Id,
                    this._orchestrationAgent.Role,
                    cancellationToken);
            }
            catch (Exception ex) when (StructuredOutputParser.IsParseFailure(ex))
            {
                await this._artefactStore.AppendEventAsync(runDirectory, new
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

            await this._artefactStore.WriteExecutionPlanAsync(runDirectory, plan, cancellationToken);

            AgentStepExecutor.StepExecutionResult stepResult = await this._agentStepExecutor.ExecuteAsync(
                plan,
                adapter,
                request,
                runId,
                runDirectory,
                progress,
                cancellationToken);

            string frontendPlan = stepResult.FrontendPlan;
            IReadOnlyList<string> filesTouched = stepResult.FilesTouched;
            ArchitectureReview review = stepResult.Review;

            IReadOnlyList<string>? architectureLanguages = plan.Steps.LastOrDefault(s => s.Agent == "Architecture")?.Languages;
            (review, filesTouched) = await this._architectureReviewLoop.RunAsync(
                new ArchitectureLoopRequest(
                    IterationStrategy: plan.IterationStrategy,
                    InitialReview: review,
                    FilesTouched: filesTouched,
                    ArchitectureLanguages: architectureLanguages,
                    RunRequest: request),
                adapter,
                progress,
                cancellationToken);

            if (review.RequiredActions.Contains(ArchitectureReviewLoop.NO_PROGRESS_BLOCKED_STATUS, StringComparer.OrdinalIgnoreCase))
            {
                await this._artefactStore.AppendEventAsync(runDirectory, new
                {
                    runId,
                    source = "architecture-loop",
                    status = "blocked",
                    message = "Architecture review iterations produced identical findings; loop stopped early."
                }, cancellationToken);
            }

            await this._artefactStore.WriteArchitectureReviewAsync(runDirectory, review, cancellationToken);

            RunArtefacts artefacts = await this.RunBuildAndValidateAsync(
                request, plan, review, frontendPlan, filesTouched, adapter, runId, runDirectory, progress, cancellationToken);

            await sessionEventCts.CancelAsync();
            await sessionEventPump;
            return artefacts;
        }
        finally
        {
            this._runContextAccessor.SetCurrent(null);
        }
    }

    /// <summary>
    /// Runs the final build command, validates completion criteria against the architecture review,
    /// writes the summary and run log, and returns the run artefacts.
    /// </summary>
    private async Task<RunArtefacts> RunBuildAndValidateAsync(
        RunRequest request,
        ExecutionPlan plan,
        ArchitectureReview review,
        string frontendPlan,
        IReadOnlyList<string> filesTouched,
        IWorkspaceAdapter adapter,
        string runId,
        string runDirectory,
        IProgress<RuntimeProgressEvent>? progress,
        CancellationToken cancellationToken)
    {
        BuildCommandSelection finalBuildSelection = BuildCommandInference.Select(
            adapter.RootPath,
            request.BuildCommand,
            request.WorkspaceMode,
            request.ProjectName);
        await this._artefactStore.AppendEventAsync(runDirectory, new
        {
            runId,
            source = "build-selection",
            message = "Final build command selection",
            buildCommand = finalBuildSelection.Command,
            inferred = finalBuildSelection.Inferred,
            reason = finalBuildSelection.Reason
        }, cancellationToken);

        BuildResult buildResult = await this._buildRunner.RunAsync(finalBuildSelection.Command, adapter.RootPath, cancellationToken);
        await this._artefactStore.WriteBuildResultAsync(runDirectory, buildResult, cancellationToken);
        progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, "build", buildResult.Passed ? "Build passed" : "Build failed or not executed"));

        bool buildCommandConfigured = !string.IsNullOrWhiteSpace(request.BuildCommand);
        bool completed = await this._orchestrationAgent.ValidateCompletionAsync(
            new CompletionValidationRequest(
                Plan: plan,
                Review: review,
                BuildPassed: buildResult.Passed,
                BuildCommandConfigured: buildCommandConfigured,
                ModelOverrides: request.ModelOverrides),
            this._orchestrationAgent.Id,
            this._orchestrationAgent.Role,
            cancellationToken);
        string summary = $"""
            # Final Summary
            - Completed: {completed}
            - FrontendPlan: {frontendPlan}
            - FilesTouched: {string.Join(", ", filesTouched)}
            - BuildExecuted: {buildResult.Executed}
            - BuildPassed: {buildResult.Passed}
            """;
        await this._artefactStore.WriteFinalSummaryAsync(runDirectory, summary, cancellationToken);

        string[] modelOverrides = request.ModelOverrides?.Select(pair => $"{pair.Key}={pair.Value}").ToArray() ?? Array.Empty<string>();
        IReadOnlyList<CopilotModelUsage> usage = this._copilotClient.GetUsageSnapshot();

        await this._runStore.WriteRunLogAsync(runDirectory, new
        {
            status = completed ? "completed" : "incomplete",
            request.WorkspaceMode,
            request.Workflow,
            modelOverrides,
            agents = new[]
            {
                new { role = "orchestration", model = this._orchestrationAgent.ResolveModel(request.ModelOverrides) },
                new { role = "frontend", model = this._frontendAgent.ResolveModel(request.ModelOverrides) },
                new { role = "builder", model = this._builderAgent.ResolveModel(request.ModelOverrides) },
                new { role = "style", model = this._styleAgent.ResolveModel(request.ModelOverrides) },
                new { role = "architecture", model = this._architectureAgent.ResolveModel(request.ModelOverrides) }
            },
            copilotUsage = usage
        }, cancellationToken);

        await this._artefactStore.AppendEventAsync(runDirectory, new { runId, source = ORCHESTRATOR_SOURCE, message = "Run completed" }, cancellationToken);
        progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, ORCHESTRATOR_SOURCE, "Run completed"));

        return new RunArtefacts(runId, runDirectory);
    }

    /// <summary>
    /// Groups the agent instances required by <see cref="OrchestratorRuntime"/>.
    /// </summary>
    public sealed class OrchestratorAgentDependencies
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="OrchestratorAgentDependencies"/> class.
        /// </summary>
        /// <param name="orchestrationAgent">The orchestration planning agent.</param>
        /// <param name="frontendAgent">The frontend implementation agent.</param>
        /// <param name="builderAgent">The backend builder agent.</param>
        /// <param name="styleAgent">The coding style enforcement agent.</param>
        /// <param name="architectureAgent">The architecture review agent.</param>
        public OrchestratorAgentDependencies(
            OrchestrationAgent orchestrationAgent,
            FrontendAgent frontendAgent,
            BuilderAgent builderAgent,
            StyleAgent styleAgent,
            ArchitectureAgent architectureAgent)
        {
            this.OrchestrationAgent = orchestrationAgent;
            this.FrontendAgent = frontendAgent;
            this.BuilderAgent = builderAgent;
            this.StyleAgent = styleAgent;
            this.ArchitectureAgent = architectureAgent;
        }

        /// <summary>Gets the orchestration planning agent.</summary>
        public OrchestrationAgent OrchestrationAgent { get; }
        /// <summary>Gets the frontend implementation agent.</summary>
        public FrontendAgent FrontendAgent { get; }
        /// <summary>Gets the backend builder agent.</summary>
        public BuilderAgent BuilderAgent { get; }
        /// <summary>Gets the coding style enforcement agent.</summary>
        public StyleAgent StyleAgent { get; }
        /// <summary>Gets the architecture review agent.</summary>
        public ArchitectureAgent ArchitectureAgent { get; }
    }

    /// <summary>
    /// Groups the service instances required by <see cref="OrchestratorRuntime"/>.
    /// </summary>
    public sealed class OrchestratorServiceDependencies
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="OrchestratorServiceDependencies"/> class.
        /// </summary>
        /// <param name="runStore">Store for managing run directories and logs.</param>
        /// <param name="artefactStore">Store for persisting run artefacts.</param>
        /// <param name="copilotClient">Client for Copilot completions.</param>
        /// <param name="sessionEventStream">Stream for Copilot session lifecycle events.</param>
        /// <param name="buildRunner">Runner for executing build commands.</param>
        /// <param name="runContextAccessor">Accessor for the current run context.</param>
        public OrchestratorServiceDependencies(
            IRunStore runStore,
            IArtefactStore artefactStore,
            ICopilotClient copilotClient,
            ICopilotSessionEventStream sessionEventStream,
            IBuildRunner buildRunner,
            IRunContextAccessor runContextAccessor)
        {
            this.RunStore = runStore;
            this.ArtefactStore = artefactStore;
            this.CopilotClient = copilotClient;
            this.SessionEventStream = sessionEventStream;
            this.BuildRunner = buildRunner;
            this.RunContextAccessor = runContextAccessor;
        }

        /// <summary>Gets the run store.</summary>
        public IRunStore RunStore { get; }
        /// <summary>Gets the artefact store.</summary>
        public IArtefactStore ArtefactStore { get; }
        /// <summary>Gets the Copilot client.</summary>
        public ICopilotClient CopilotClient { get; }
        /// <summary>Gets the session event stream.</summary>
        public ICopilotSessionEventStream SessionEventStream { get; }
        /// <summary>Gets the build runner.</summary>
        public IBuildRunner BuildRunner { get; }
        /// <summary>Gets the run context accessor.</summary>
        public IRunContextAccessor RunContextAccessor { get; }
    }

}
