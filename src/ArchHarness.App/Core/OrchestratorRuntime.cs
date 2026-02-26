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
    private const string OrchestratorSource = "orchestrator";

    private readonly OrchestratorAgentDependencies _agentDependencies;
    private readonly ICopilotClient _copilotClient;
    private readonly IRunContextAccessor _runContextAccessor;
    private readonly ArchitectureReviewLoop _architectureReviewLoop;
    private readonly PlanExecutor _planExecutor;
    private readonly BuildValidator _buildValidator;
    private readonly RunArtifactWriter _artifactWriter;
    private readonly RunEventLogger _eventLogger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrchestratorRuntime"/> class.
    /// </summary>
    public OrchestratorRuntime(
        OrchestratorAgentDependencies agentDependencies,
        ICopilotClient copilotClient,
        IRunContextAccessor runContextAccessor,
        ArchitectureReviewLoop architectureReviewLoop,
        PlanExecutor planExecutor,
        BuildValidator buildValidator,
        RunArtifactWriter artifactWriter,
        RunEventLogger eventLogger)
    {
        _agentDependencies = agentDependencies;
        _copilotClient = copilotClient;
        _runContextAccessor = runContextAccessor;
        _architectureReviewLoop = architectureReviewLoop;
        _planExecutor = planExecutor;
        _buildValidator = buildValidator;
        _artifactWriter = artifactWriter;
        _eventLogger = eventLogger;
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

        string runDirectory = _artifactWriter.CreateRunDirectory(adapter.RootPath);
        string runId = Path.GetFileName(runDirectory);
        _runContextAccessor.SetCurrent(new RunContext(runId, runDirectory));
        using CancellationTokenSource sessionEventCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task sessionEventPump = Task.Run(async () => await _eventLogger.PumpSessionEventsAsync(runDirectory, runId, sessionEventCts.Token), CancellationToken.None);

        try
        {
            await _eventLogger.AppendEventAsync(runDirectory, new { runId, source = OrchestratorSource, message = "Run started" }, cancellationToken);
            await _eventLogger.AppendEventAsync(runDirectory, new
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
            await _eventLogger.AppendEventAsync(runDirectory, new
            {
                runId,
                source = "build-selection",
                message = "Initial build command selection",
                buildCommand = request.BuildCommand,
                inferred = initialBuildSelection.Inferred,
                reason = initialBuildSelection.Reason
            }, cancellationToken);
            progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, OrchestratorSource, "Run started"));

            PlanExecutionResult planResult;
            try
            {
                planResult = await _planExecutor.BuildAndExecuteAsync(
                    request,
                    adapter,
                    runId,
                    runDirectory,
                    progress,
                    cancellationToken);
            }
            catch (Exception ex) when (StructuredOutputParser.IsParseFailure(ex))
            {
                await _eventLogger.AppendEventAsync(runDirectory, new
                {
                    runId,
                    source = OrchestratorSource,
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

            IReadOnlyList<string>? architectureLanguages = plan.Steps.LastOrDefault(s => s.Agent == "Architecture")?.Languages;
            (review, filesTouched) = await _architectureReviewLoop.RunAsync(
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
                await _eventLogger.AppendEventAsync(runDirectory, new
                {
                    runId,
                    source = "architecture-loop",
                    status = "blocked",
                    message = "Architecture review iterations produced identical findings; loop stopped early."
                }, cancellationToken);
            }

            await _artifactWriter.WriteArchitectureReviewAsync(runDirectory, review, cancellationToken);

            BuildValidationResult validation = await _buildValidator.ExecuteAndValidateAsync(
                plan,
                review,
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
                - BuildExecuted: {validation.BuildResult.Executed}
                - BuildPassed: {validation.BuildResult.Passed}
                """;
            await _artifactWriter.WriteFinalSummaryAsync(runDirectory, summary, cancellationToken);

            string[] modelOverrides = request.ModelOverrides?.Select(pair => $"{pair.Key}={pair.Value}").ToArray() ?? Array.Empty<string>();
            IReadOnlyList<CopilotModelUsage> usage = _copilotClient.GetUsageSnapshot();

            await _artifactWriter.WriteRunLogAsync(runDirectory, new
            {
                status = validation.Completed ? "completed" : "incomplete",
                request.WorkspaceMode,
                request.Workflow,
                modelOverrides,
                agents = new[]
                {
                    new { role = "orchestration", model = _agentDependencies.OrchestrationAgent.ResolveModel(request.ModelOverrides) },
                    new { role = "frontend", model = _agentDependencies.FrontendAgent.ResolveModel(request.ModelOverrides) },
                    new { role = "builder", model = _agentDependencies.BuilderAgent.ResolveModel(request.ModelOverrides) },
                    new { role = "style", model = _agentDependencies.StyleAgent.ResolveModel(request.ModelOverrides) },
                    new { role = "architecture", model = _agentDependencies.ArchitectureAgent.ResolveModel(request.ModelOverrides) }
                },
                copilotUsage = usage
            }, cancellationToken);

            await _eventLogger.AppendEventAsync(runDirectory, new { runId, source = OrchestratorSource, message = "Run completed" }, cancellationToken);
            progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, OrchestratorSource, "Run completed"));

            await sessionEventCts.CancelAsync();
            await sessionEventPump;
            return new RunArtefacts(runId, runDirectory);
        }
        finally
        {
            _runContextAccessor.SetCurrent(null);
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
            FrontendAgent frontendAgent,
            BuilderAgent builderAgent,
            StyleAgent styleAgent,
            ArchitectureAgent architectureAgent)
        {
            OrchestrationAgent = orchestrationAgent;
            FrontendAgent = frontendAgent;
            BuilderAgent = builderAgent;
            StyleAgent = styleAgent;
            ArchitectureAgent = architectureAgent;
        }

        public OrchestrationAgent OrchestrationAgent { get; }

        public FrontendAgent FrontendAgent { get; }

        public BuilderAgent BuilderAgent { get; }

        public StyleAgent StyleAgent { get; }

        public ArchitectureAgent ArchitectureAgent { get; }
    }
}
