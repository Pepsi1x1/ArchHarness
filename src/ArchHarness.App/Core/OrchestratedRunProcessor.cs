using System.Text.Json;
using ArchHarness.App.Constants;
using ArchHarness.App.Copilot;
using ArchHarness.App.Storage;
using ArchHarness.App.Workspace;

namespace ArchHarness.App.Core;

/// <summary>
/// Represents a bootstrapped run ready for orchestration.
/// </summary>
public sealed record OrchestratedRunContext(
    IWorkspaceAdapter Adapter,
    RunRequest Request,
    PersistedRunState? ResumeState,
    BuildCommandSelection? InitialBuildSelection);

/// <summary>
/// Executes a bootstrapped run through plan execution, review loops, and finalization.
/// </summary>
public interface IOrchestratedRunProcessor
{
    /// <summary>
    /// Executes the supplied run context.
    /// </summary>
    Task<RunArtefacts> ExecuteAsync(
        OrchestratedRunContext context,
        IProgress<RuntimeProgressEvent>? progress,
        Action<string, string>? onRunContextEstablished,
        CancellationToken cancellationToken);
}

/// <summary>
/// Default implementation of <see cref="IOrchestratedRunProcessor"/>.
/// </summary>
public sealed class OrchestratedRunProcessor : IOrchestratedRunProcessor
{
    private const string RUN_COMPLETED_MESSAGE = "Run completed";
    private const string RUN_STARTED_MESSAGE = "Run started";
    private const string RUN_RESUMED_MESSAGE = "Run resumed";

    private readonly OrchestratorRunServices _services;
    private readonly RuntimeStateAccessors _stateAccessors;
    private readonly IRunCompletionValidator _completionValidator;
    private readonly IRunAgentModelUsageBuilder _agentModelUsageBuilder;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrchestratedRunProcessor"/> class.
    /// </summary>
    public OrchestratedRunProcessor(
        OrchestratorRunServices services,
        RuntimeStateAccessors stateAccessors,
        IRunCompletionValidator completionValidator,
        IRunAgentModelUsageBuilder agentModelUsageBuilder)
    {
        this._services = services;
        this._stateAccessors = stateAccessors;
        this._completionValidator = completionValidator;
        this._agentModelUsageBuilder = agentModelUsageBuilder;
    }

    /// <inheritdoc />
    public async Task<RunArtefacts> ExecuteAsync(
        OrchestratedRunContext context,
        IProgress<RuntimeProgressEvent>? progress,
        Action<string, string>? onRunContextEstablished,
        CancellationToken cancellationToken)
    {
        IWorkspaceAdapter adapter = context.Adapter;
        RunRequest request = context.Request;
        PersistedRunState? resumeState = context.ResumeState;

        string runDirectory = resumeState?.RunDirectory ?? this._services.RunInfrastructure.ArtifactWriter.CreateRunDirectory(adapter.RootPath);
        string runId = resumeState?.RunId ?? Path.GetFileName(runDirectory);
        RunStateCheckpoint checkpoint = new(runId, runDirectory, adapter.RootPath, request);

        onRunContextEstablished?.Invoke(runId, runDirectory);
        this.InitializeRuntimeContext(runId, runDirectory, adapter.RootPath, request);

        using CancellationTokenSource sessionEventCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using CancellationTokenSource agentEventCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task sessionEventPump = Task.Run(() => this._services.RunInfrastructure.EventLogger.PumpSessionEventsAsync(runDirectory, runId, sessionEventCts.Token), CancellationToken.None);
        Task agentEventPump = Task.Run(() => this._services.RunInfrastructure.EventLogger.PumpAgentEventsAsync(runDirectory, runId, agentEventCts.Token), CancellationToken.None);

        try
        {
            if (resumeState is null)
            {
                await this.WriteRunAcceptedAsync(runDirectory, runId, request, context.InitialBuildSelection, cancellationToken).ConfigureAwait(false);
                await this.WriteRunStateAsync(
                    checkpoint,
                    new RunProgressSnapshot(
                        Array.Empty<int>(),
                        0,
                        string.Empty,
                        Array.Empty<string>(),
                        new ArchitectureReview(Array.Empty<ArchitectureFinding>(), Array.Empty<string>()),
                        new SecurityReview(Array.Empty<SecurityFinding>(), Array.Empty<string>())),
                    RunPhases.PLANNING,
                    null,
                    cancellationToken).ConfigureAwait(false);
                progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, WellKnownSources.ORCHESTRATOR, RUN_STARTED_MESSAGE));
            }
            else
            {
                await this._services.RunInfrastructure.EventLogger.AppendEventAsync(runDirectory, new { runId, source = WellKnownSources.ORCHESTRATOR, message = RUN_RESUMED_MESSAGE }, cancellationToken).ConfigureAwait(false);
                progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, WellKnownSources.ORCHESTRATOR, RUN_RESUMED_MESSAGE));
            }

            (ExecutionPlan plan, PlanExecutionResult planResult) = await this.ExecutePlanAsync(context, progress, runId, runDirectory, cancellationToken).ConfigureAwait(false);

            string frontendPlan = planResult.StepResult.FrontendPlan;
            IReadOnlyList<string> filesTouched = planResult.StepResult.FilesTouched;
            ArchitectureReview review = planResult.StepResult.Review;
            SecurityReview securityReview = planResult.StepResult.SecurityReview;

            (review, securityReview, filesTouched) = await this.RunArchitectureLoopAsync(
                context,
                plan,
                adapter,
                progress,
                filesTouched,
                review,
                securityReview,
                runId,
                runDirectory,
                cancellationToken).ConfigureAwait(false);

            await this.FinalizeRunAsync(
                checkpoint,
                request,
                plan,
                frontendPlan,
                filesTouched,
                review,
                securityReview,
                resumeState?.ReviewIteration ?? 0,
                progress,
                cancellationToken).ConfigureAwait(false);

            await sessionEventCts.CancelAsync().ConfigureAwait(false);
            await agentEventCts.CancelAsync().ConfigureAwait(false);
            await DrainPumpAsync(sessionEventPump).ConfigureAwait(false);
            await DrainPumpAsync(agentEventPump).ConfigureAwait(false);
            return new RunArtefacts(runId, runDirectory);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await this.WriteTerminalRunStateAsync(runDirectory, RunStatuses.CANCELED, RunTerminalPhases.CANCELED, "Run canceled before completion.").ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            await this.WriteTerminalRunStateAsync(runDirectory, RunStatuses.FAILED, RunTerminalPhases.FAILED, ex.Message).ConfigureAwait(false);
            throw;
        }
        finally
        {
            await sessionEventCts.CancelAsync().ConfigureAwait(false);
            await agentEventCts.CancelAsync().ConfigureAwait(false);
            await DrainPumpAsync(sessionEventPump).ConfigureAwait(false);
            await DrainPumpAsync(agentEventPump).ConfigureAwait(false);
            this.ClearRuntimeContext();
        }
    }

    private void InitializeRuntimeContext(string runId, string runDirectory, string workspaceRoot, RunRequest request)
    {
        string normalizedPermissionMode = PermissionHandlerModes.Normalize(request.PermissionHandlerMode);
        ReviewLoopAgentSelection reviewLoopAgents = request.ReviewLoopAgents ?? this._services.SessionContext.AgentsOptions.GetReviewLoopAgentSelection();
        this._stateAccessors.PermissionHandlerMode.SetCurrent(normalizedPermissionMode);
        this._stateAccessors.ReviewLoopAgentSelection.SetCurrent(reviewLoopAgents);
        this._stateAccessors.WorkspaceRoot.SetCurrent(workspaceRoot);
        this._services.RunInfrastructure.RunContextAccessor.SetCurrent(new RunContext(runId, runDirectory));
    }

    private void ClearRuntimeContext()
    {
        this._stateAccessors.PermissionHandlerMode.SetCurrent(null);
        this._stateAccessors.ReviewLoopAgentSelection.SetCurrent(null);
        this._services.RunInfrastructure.RunContextAccessor.SetCurrent(null);
        this._stateAccessors.WorkspaceRoot.SetCurrent(null);
    }

    private async Task WriteRunAcceptedAsync(
        string runDirectory,
        string runId,
        RunRequest request,
        BuildCommandSelection? initialBuildSelection,
        CancellationToken cancellationToken)
    {
        ReviewLoopAgentSelection reviewLoopAgents = request.ReviewLoopAgents ?? this._services.SessionContext.AgentsOptions.GetReviewLoopAgentSelection();

        await this._services.RunInfrastructure.EventLogger.AppendEventAsync(runDirectory, new { runId, source = WellKnownSources.ORCHESTRATOR, message = RUN_STARTED_MESSAGE }, cancellationToken).ConfigureAwait(false);
        await this._services.RunInfrastructure.EventLogger.AppendEventAsync(runDirectory, new
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
        }, cancellationToken).ConfigureAwait(false);

        if (initialBuildSelection is not null)
        {
            await this._services.RunInfrastructure.EventLogger.AppendEventAsync(runDirectory, new
            {
                runId,
                source = "build-selection",
                message = "Initial build command selection",
                buildCommand = request.BuildCommand,
                inferred = initialBuildSelection.Inferred,
                reason = initialBuildSelection.Reason
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<(ExecutionPlan Plan, PlanExecutionResult Result)> ExecutePlanAsync(
        OrchestratedRunContext context,
        IProgress<RuntimeProgressEvent>? progress,
        string runId,
        string runDirectory,
        CancellationToken cancellationToken)
    {
        IWorkspaceAdapter adapter = context.Adapter;
        RunRequest request = context.Request;
        PersistedRunState? resumeState = context.ResumeState;
        try
        {
            if (resumeState is null)
            {
                PlanExecutionResult built = await this._services.RunPhases.PlanExecutor.BuildAndExecuteAsync(
                    request,
                    adapter,
                    runId,
                    runDirectory,
                    progress,
                    cancellationToken).ConfigureAwait(false);
                return (built.Plan, built);
            }

            string executionPlanPath = FileSystemStorageHelper.GetRunFilePath(runDirectory, "ExecutionPlan.json");
            if (!File.Exists(executionPlanPath))
            {
                PlanExecutionResult rebuilt = await this._services.RunPhases.PlanExecutor.BuildAndExecuteAsync(
                    request,
                    adapter,
                    runId,
                    runDirectory,
                    progress,
                    cancellationToken).ConfigureAwait(false);
                return (rebuilt.Plan, rebuilt);
            }

            ExecutionPlan plan = JsonSerializer.Deserialize<ExecutionPlan>(
                    await File.ReadAllTextAsync(executionPlanPath, cancellationToken).ConfigureAwait(false),
                    JsonDefaults.INDENTED)
                ?? throw new InvalidOperationException($"Unable to deserialize persisted execution plan for run '{runId}'.");

            PlanExecutionResult resumed = await this._services.RunPhases.PlanExecutor.ExecuteExistingPlanAsync(
                plan,
                request,
                adapter,
                new PlanResumeContext(runId, runDirectory, resumeState),
                progress,
                cancellationToken).ConfigureAwait(false);
            return (plan, resumed);
        }
        catch (Exception ex) when (StructuredOutputParser.IsParseFailure(ex))
        {
            await this._services.RunInfrastructure.EventLogger.AppendEventAsync(runDirectory, new
            {
                runId,
                source = WellKnownSources.ORCHESTRATOR,
                status = RunEventStatuses.FAILED,
                failureType = "parse_error",
                stage = RunPhases.PLANNING,
                message = ex.Message
            }, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<(ArchitectureReview Review, SecurityReview SecurityReview, IReadOnlyList<string> FilesTouched)> RunArchitectureLoopAsync(
        OrchestratedRunContext context,
        ExecutionPlan plan,
        IWorkspaceAdapter adapter,
        IProgress<RuntimeProgressEvent>? progress,
        IReadOnlyList<string> filesTouched,
        ArchitectureReview review,
        SecurityReview securityReview,
        string runId,
        string runDirectory,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string>? architectureLanguages = plan.Steps.LastOrDefault(s => s.Agent == "Architecture")?.Languages;
        IReadOnlyList<string>? securityLanguages = plan.Steps.LastOrDefault(s => s.Agent == "Security")?.Languages;
        PersistedRunState? resumeState = context.ResumeState;

        (review, securityReview, filesTouched) = await this._services.RunPhases.ArchitectureReviewLoop.RunAsync(
            new ArchitectureLoopRequest(
                plan.IterationStrategy,
                review,
                securityReview,
                filesTouched,
                architectureLanguages,
                securityLanguages,
                context.Request,
                resumeState?.ReviewIteration ?? 0),
            adapter,
            progress,
            cancellationToken).ConfigureAwait(false);

        if (review.RequiredActions.Contains(ArchitectureReviewLoop.NO_PROGRESS_BLOCKED_STATUS, StringComparer.OrdinalIgnoreCase)
            || securityReview.RequiredActions.Contains(ArchitectureReviewLoop.NO_PROGRESS_BLOCKED_STATUS, StringComparer.OrdinalIgnoreCase))
        {
            await this._services.RunInfrastructure.EventLogger.AppendEventAsync(
                runDirectory,
                new
                {
                    runId,
                    source = WellKnownSources.ARCHITECTURE_LOOP,
                    status = RunEventStatuses.BLOCKED,
                    message = "Architecture review iterations produced identical findings; loop stopped early."
                },
                cancellationToken).ConfigureAwait(false);
        }

        return (review, securityReview, filesTouched);
    }

    private async Task FinalizeRunAsync(
        RunStateCheckpoint checkpoint,
        RunRequest request,
        ExecutionPlan plan,
        string frontendPlan,
        IReadOnlyList<string> filesTouched,
        ArchitectureReview review,
        SecurityReview securityReview,
        int reviewIteration,
        IProgress<RuntimeProgressEvent>? progress,
        CancellationToken cancellationToken)
    {
        await this._services.RunInfrastructure.ArtifactWriter.WriteArchitectureReviewAsync(checkpoint.RunDirectory, review, cancellationToken).ConfigureAwait(false);
        await this._services.RunInfrastructure.ArtifactWriter.WriteSecurityReviewAsync(checkpoint.RunDirectory, securityReview, cancellationToken).ConfigureAwait(false);
        await this.WriteRunStateAsync(
            checkpoint,
            new RunProgressSnapshot(plan.Steps.Select(step => step.Id).ToArray(), reviewIteration, frontendPlan, filesTouched, review, securityReview),
            RunPhases.FINALIZING,
            null,
            cancellationToken).ConfigureAwait(false);

        bool completed = await this._completionValidator.ValidateAsync(plan, review, securityReview, request.ModelOverrides, cancellationToken).ConfigureAwait(false);
        await this._services.RunInfrastructure.ArtifactWriter.WriteFinalSummaryAsync(
            checkpoint.RunDirectory,
            BuildFinalSummary(frontendPlan, filesTouched, review, securityReview, completed),
            cancellationToken).ConfigureAwait(false);

        string[] modelOverrides = request.ModelOverrides?.Select(pair => $"{pair.Key}={pair.Value}").ToArray() ?? Array.Empty<string>();
        IReadOnlyList<CopilotModelUsage> usage = this._services.SessionContext.CopilotClient.GetUsageSnapshot();
        object[] agentModelUsage = this._agentModelUsageBuilder.Build(request.ModelOverrides);

        await this._services.RunInfrastructure.ArtifactWriter.WriteRunLogAsync(checkpoint.RunDirectory, new
        {
            status = completed ? RunStatuses.COMPLETED : "incomplete",
            projectId = request.ProjectId,
            projectName = request.ProjectName,
            runTitle = request.RunTitle,
            request.WorkspaceMode,
            request.Workflow,
            request.PermissionHandlerMode,
            modelOverrides,
            agents = agentModelUsage,
            copilotUsage = usage
        }, cancellationToken).ConfigureAwait(false);

        await this._services.RunInfrastructure.EventLogger.AppendEventAsync(checkpoint.RunDirectory, new { runId = checkpoint.RunId, source = WellKnownSources.ORCHESTRATOR, message = RUN_COMPLETED_MESSAGE }, cancellationToken).ConfigureAwait(false);
        await this.WriteRunStateAsync(
            checkpoint,
            new RunProgressSnapshot(plan.Steps.Select(step => step.Id).ToArray(), reviewIteration, frontendPlan, filesTouched, review, securityReview),
            RunTerminalPhases.COMPLETED,
            null,
            cancellationToken,
            RunStatuses.COMPLETED).ConfigureAwait(false);
        progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, WellKnownSources.ORCHESTRATOR, RUN_COMPLETED_MESSAGE));
    }

    private async Task WriteTerminalRunStateAsync(string runDirectory, string status, string phase, string failureMessage)
    {
        PersistedRunState? existingState = this._services.SessionContext.RunStateStore.GetState(runDirectory);
        if (existingState is null)
        {
            return;
        }

        await this._services.SessionContext.RunStateStore.WriteStateAsync(
            runDirectory,
            existingState with
            {
                Status = status,
                Phase = phase,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                FailureMessage = failureMessage
            },
            CancellationToken.None).ConfigureAwait(false);
    }

    private Task WriteRunStateAsync(
        RunStateCheckpoint checkpoint,
        RunProgressSnapshot progress,
        string phase,
        string? failureMessage,
        CancellationToken cancellationToken,
        string status = RunStatuses.RUNNING)
    {
        PersistedRunState? existingState = this._services.SessionContext.RunStateStore.GetState(checkpoint.RunDirectory);
        return this._services.SessionContext.RunStateStore.WriteStateAsync(
            checkpoint.RunDirectory,
            new PersistedRunState(
                checkpoint.RunId,
                checkpoint.RunDirectory,
                checkpoint.WorkspaceRoot,
                status,
                phase,
                existingState?.StartedAtUtc ?? DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                checkpoint.Request,
                progress.CompletedStepIds,
                progress.ReviewIteration,
                progress.FrontendPlan,
                progress.FilesTouched.ToArray(),
                progress.Review,
                progress.SecurityReview,
                failureMessage),
            cancellationToken);
    }

    private static string BuildFinalSummary(
        string frontendPlan,
        IReadOnlyList<string> filesTouched,
        ArchitectureReview review,
        SecurityReview securityReview,
        bool completed)
    {
        int securityHighCount = securityReview.Findings.Count(f => string.Equals(f.Severity, "high", StringComparison.OrdinalIgnoreCase));
        int architectureHighCount = review.Findings.Count(f => string.Equals(f.Severity, "high", StringComparison.OrdinalIgnoreCase));
        string filesTouchedList = string.Join(", ", filesTouched);
        return $"""
            # Final Summary
            - Completed: {completed}
            - FrontendPlan: {frontendPlan}
            - FilesTouched: {filesTouchedList}
            - SecurityHighFindings: {securityHighCount}
            - ArchitectureHighFindings: {architectureHighCount}
            """;
    }

    private static async Task DrainPumpAsync(Task pumpTask)
    {
        try
        {
            await pumpTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected if the run is shutting down before the pump drains.
        }
    }

    private sealed record RunStateCheckpoint(string RunId, string RunDirectory, string WorkspaceRoot, RunRequest Request);

    private sealed record RunProgressSnapshot(
        int[] CompletedStepIds,
        int ReviewIteration,
        string FrontendPlan,
        IReadOnlyList<string> FilesTouched,
        ArchitectureReview Review,
        SecurityReview SecurityReview);
}
