using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArchHarness.App.Agents;
using ArchHarness.App.Constants;
using ArchHarness.App.Copilot;
using ArchHarness.App.Storage;
using ArchHarness.App.Workspace;
using GitHub.Copilot.SDK;

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
    private const string PLANNING_COMPLETED_MESSAGE = "Planning completed";
    private const int MAX_CLARIFICATION_ROUNDS = 3;
    private const int MAX_ORCHESTRATOR_REPLANNING_CYCLES = 2;
    private const string RUN_STARTED_MESSAGE = "Run started";
    private const string RUN_RESUMED_MESSAGE = "Run resumed";
    private const string NONE_LABEL = "(none)";

    private readonly OrchestratorRunServices _services;
    private readonly RuntimeStateAccessors _stateAccessors;
    private readonly IPlanApprovalBridge? _approvalBridge;
    private readonly ICopilotUserInputBridge? _userInputBridge;
    private readonly OrchestratorPlanningServices _planningServices;
    private readonly WikiDocRunServices _wikiDocServices;
    private readonly ArchHarness.App.Storage.PlanningSessionRecorder? _planningSessionRecorder;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrchestratedRunProcessor"/> class.
    /// </summary>
    public OrchestratedRunProcessor(
        OrchestratorRunServices services,
        RuntimeStateAccessors stateAccessors,
        OrchestratorPlanningServices planningServices,
        WikiDocRunServices wikiDocServices,
        IPlanApprovalBridge? approvalBridge = null,
        ICopilotUserInputBridge? userInputBridge = null,
        ArchHarness.App.Storage.PlanningSessionRecorder? planningSessionRecorder = null)
    {
        this._services = services;
        this._stateAccessors = stateAccessors;
        this._planningServices = planningServices;
        this._wikiDocServices = wikiDocServices;
        this._approvalBridge = approvalBridge;
        this._userInputBridge = userInputBridge;
        this._planningSessionRecorder = planningSessionRecorder;
    }

    /// <inheritdoc />
    public async Task<RunArtefacts> ExecuteAsync(
        OrchestratedRunContext context,
        IProgress<RuntimeProgressEvent>? progress,
        Action<string, string>? onRunContextEstablished,
        CancellationToken cancellationToken)
    {
        IWorkspaceAdapter adapter = context.Adapter;
        RunRequest request = RunRequestWorkflowDefaults.Apply(context.Request);
        OrchestratedRunContext activeContext = context with { Request = request };
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
                    IsWikiDocWorkflow(request.Workflow) ? RunPhases.EXECUTING_PLAN : RunPhases.PLANNING,
                    null,
                    cancellationToken).ConfigureAwait(false);
                progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, WellKnownSources.ORCHESTRATOR, RUN_STARTED_MESSAGE));
                await this.EnsurePlanningSessionForRunStartAsync(request, adapter.RootPath, runId, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await this._services.RunInfrastructure.EventLogger.AppendEventAsync(runDirectory, new { runId, source = WellKnownSources.ORCHESTRATOR, message = RUN_RESUMED_MESSAGE }, cancellationToken).ConfigureAwait(false);
                progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, WellKnownSources.ORCHESTRATOR, RUN_RESUMED_MESSAGE));
            }

            if (IsWikiDocWorkflow(request.Workflow))
            {
                await this.ExecuteWikiDocWorkflowAsync(
                    checkpoint,
                    request,
                    progress,
                    cancellationToken).ConfigureAwait(false);

                await sessionEventCts.CancelAsync().ConfigureAwait(false);
                await agentEventCts.CancelAsync().ConfigureAwait(false);
                await DrainPumpAsync(sessionEventPump).ConfigureAwait(false);
                await DrainPumpAsync(agentEventPump).ConfigureAwait(false);
                return new RunArtefacts(runId, runDirectory);
            }

            (ExecutionPlan plan, PlanExecutionResult planResult, ClarificationSpec? spec, IReadOnlyList<ClarificationAnswer> clarificationAnswers) = await this.ExecutePlanAsync(activeContext, progress, runId, runDirectory, cancellationToken).ConfigureAwait(false);

            if (IsPlanningWorkflow(request.Workflow))
            {
                await this.FinalizePlanningRunAsync(
                    checkpoint,
                    request,
                    plan,
                    spec,
                    clarificationAnswers,
                    progress,
                    cancellationToken).ConfigureAwait(false);

                await sessionEventCts.CancelAsync().ConfigureAwait(false);
                await agentEventCts.CancelAsync().ConfigureAwait(false);
                await DrainPumpAsync(sessionEventPump).ConfigureAwait(false);
                await DrainPumpAsync(agentEventPump).ConfigureAwait(false);
                return new RunArtefacts(runId, runDirectory);
            }

            string frontendPlan = planResult.StepResult.FrontendPlan;
            IReadOnlyList<string> filesTouched = planResult.StepResult.FilesTouched;
            ArchitectureReview review = planResult.StepResult.Review;
            SecurityReview securityReview = planResult.StepResult.SecurityReview;
            BuildOutcome? lastBuildOutcome = planResult.StepResult.LastBuildOutcome;

            ReplanningScope replanningScope = new(activeContext, checkpoint, adapter, progress, spec, clarificationAnswers);
            ReplanningRunState replanningState = await this.RunReviewReplanningCyclesAsync(
                replanningScope,
                new ReplanningRunState(plan, frontendPlan, filesTouched, review, securityReview, lastBuildOutcome),
                cancellationToken).ConfigureAwait(false);
            plan = replanningState.Plan;
            frontendPlan = replanningState.FrontendPlan;
            filesTouched = replanningState.FilesTouched;
            review = replanningState.Review;
            securityReview = replanningState.SecurityReview;
            lastBuildOutcome = replanningState.LastBuildOutcome;

            await this.FinalizeRunAsync(
                new ReplanningScope(activeContext, checkpoint, adapter, progress, spec, clarificationAnswers),
                new ReplanningRunState(plan, frontendPlan, filesTouched, review, securityReview, lastBuildOutcome),
                resumeState?.ReviewIteration ?? 0,
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
        catch (OperationCanceledException ex)
        {
            await this.WriteTerminalRunStateAsync(runDirectory, RunStatuses.CANCELED, RunTerminalPhases.CANCELED, ex.Message).ConfigureAwait(false);
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
            // Flush the channel-backed JSONL writers after the pumps stop so pending
            // events are durably written before we clear the runtime context.
            try
            {
                await this._services.RunInfrastructure.EventLogger.CompleteRunAsync(runDirectory, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort: telemetry flush must not mask the original run outcome.
            }
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
            planningSourceRunId = request.PlanningSourceRunId,
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

    private async Task<(ExecutionPlan Plan, PlanExecutionResult Result, ClarificationSpec? Spec, IReadOnlyList<ClarificationAnswer> ClarificationAnswers)> ExecutePlanAsync(
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
            // Resume path: if we have a persisted plan, execute from checkpoint.
            if (resumeState is not null)
            {
                string executionPlanPath = FileSystemStorageHelper.GetRunFilePath(runDirectory, "ExecutionPlan.json");
                if (File.Exists(executionPlanPath))
                {
                    ExecutionPlan plan = JsonSerializer.Deserialize<ExecutionPlan>(
                            await File.ReadAllTextAsync(executionPlanPath, cancellationToken).ConfigureAwait(false),
                            JsonDefaults.INDENTED)
                        ?? throw new InvalidOperationException($"Unable to deserialize persisted execution plan for run '{runId}'.");

                    // Resume from plan-approval if the plan was built but not yet approved.
                    if (string.Equals(resumeState.Phase, RunPhases.PLAN_APPROVAL, StringComparison.OrdinalIgnoreCase))
                    {
                        ClarificationSpec? resumeSpec = resumeState.Spec;
                        plan = await this.RunPlanningApprovalLoopAsync(
                            new RunStateCheckpoint(runId, runDirectory, adapter.RootPath, request),
                            adapter,
                            plan,
                            resumeSpec,
                            resumeState.ClarificationAnswers ?? Array.Empty<ClarificationAnswer>(),
                            progress,
                            cancellationToken).ConfigureAwait(false);
                    }

                    if (IsPlanningWorkflow(request.Workflow))
                    {
                        return (plan, CreatePlanningPlanExecutionResult(plan), resumeState.Spec, resumeState.ClarificationAnswers ?? Array.Empty<ClarificationAnswer>());
                    }

                    PlanExecutionResult resumed = await this._services.RunPhases.PlanExecutor.ExecuteExistingPlanAsync(
                        plan,
                        request,
                        adapter,
                        new PlanResumeContext(runId, runDirectory, resumeState),
                        progress,
                        cancellationToken).ConfigureAwait(false);
                    return (plan, resumed, resumeState.Spec, resumeState.ClarificationAnswers ?? Array.Empty<ClarificationAnswer>());
                }
            }

            if (!string.IsNullOrWhiteSpace(request.PlanningSourceRunId))
            {
                (ExecutionPlan seededPlan, ClarificationSpec? seededSpec, IReadOnlyList<ClarificationAnswer> seededAnswers) = await this.LoadPlanningSourcePlanAsync(
                    request,
                    adapter,
                    runId,
                    runDirectory,
                    cancellationToken).ConfigureAwait(false);

                PlanExecutionResult seededResult = await this._services.RunPhases.PlanExecutor.ExecuteApprovedPlanAsync(
                    seededPlan,
                    request,
                    adapter,
                    new StepExecutionContext(runId, runDirectory, null),
                    progress,
                    cancellationToken).ConfigureAwait(false);
                return (seededPlan, seededResult, seededSpec, seededAnswers);
            }

            // Fresh run: clarification/spec → plan → approval → execution.

            // Phase: Clarification/Spec generation
            ClarificationSpec? spec = null;
            IReadOnlyList<ClarificationAnswer> clarificationAnswers = resumeState?.ClarificationAnswers ?? Array.Empty<ClarificationAnswer>();
            if (IsPlanningWorkflow(request.Workflow))
            {
                PlanningAgent planningAgent = this.ResolveInitialPlanner();
                progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, WellKnownSources.ORCHESTRATOR, "Generating clarification spec"));
                (spec, clarificationAnswers) = await this.RunClarificationLoopAsync(
                    new RunStateCheckpoint(runId, runDirectory, adapter.RootPath, request),
                    clarificationAnswers,
                    planningAgent,
                    progress,
                    cancellationToken).ConfigureAwait(false);
            }

            // Phase: Plan building
            ExecutionPlan builtPlan = await this._services.RunPhases.PlanExecutor.BuildPlanAsync(
                request,
                adapter,
                runId,
                runDirectory,
                new PlanningContext(
                    spec,
                    clarificationAnswers,
                    PlanRevisionRequest: null,
                    ConversationHistory: this.GetPlanningConversationHistory(request, adapter.RootPath, runId),
                    PlanningSessionId: ResolvePlanningSessionId(request, runId)),
                cancellationToken).ConfigureAwait(false);
            progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, WellKnownSources.ORCHESTRATOR, "Execution plan built"));

            // Phase: Plan approval (when bridge is available)
            if (IsPlanningWorkflow(request.Workflow))
            {
                builtPlan = await this.RunPlanningApprovalLoopAsync(
                    new RunStateCheckpoint(runId, runDirectory, adapter.RootPath, request),
                    adapter,
                    builtPlan,
                    spec,
                    clarificationAnswers,
                    progress,
                    cancellationToken).ConfigureAwait(false);
            }

            if (IsPlanningWorkflow(request.Workflow))
            {
                return (builtPlan, CreatePlanningPlanExecutionResult(builtPlan), spec, clarificationAnswers);
            }

            // Phase: Execution
            PlanExecutionResult result = await this._services.RunPhases.PlanExecutor.ExecuteApprovedPlanAsync(
                builtPlan,
                request,
                adapter,
                new StepExecutionContext(runId, runDirectory, null),
                progress,
                cancellationToken).ConfigureAwait(false);

            return (builtPlan, result, spec, clarificationAnswers);
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

    private async Task<PlanApprovalInteractionResult?> RequestPlanApprovalAsync(
        RunRequest request,
        ClarificationSpec? spec,
        ExecutionPlan plan,
        string runId,
        string runDirectory,
        IProgress<RuntimeProgressEvent>? progress,
        CancellationToken cancellationToken)
    {
        if (this._approvalBridge is null)
        {
            return new PlanApprovalInteractionResult(
                new PlanApproval(PlanApprovalDecisions.APPROVED, DateTimeOffset.UtcNow, string.Empty),
                Attachments: null);
        }

        RunStateCheckpoint checkpoint = new(runId, runDirectory, this._stateAccessors.WorkspaceRoot.Current ?? string.Empty,
            this._services.SessionContext.RunStateStore.GetState(runDirectory)?.Request
            ?? throw new InvalidOperationException("Cannot approve plan: no run state found."));

        // Persist plan-approval phase so it's resumable.
        PersistedRunState? existingState = this._services.SessionContext.RunStateStore.GetState(runDirectory);
        await this.WriteRunStateAsync(
            checkpoint,
            new RunProgressSnapshot(
                existingState?.CompletedStepIds ?? Array.Empty<int>(),
                existingState?.ReviewIteration ?? 0,
                existingState?.FrontendPlan ?? string.Empty,
                existingState?.FilesTouched ?? Array.Empty<string>(),
                existingState?.Review ?? new ArchitectureReview(Array.Empty<ArchitectureFinding>(), Array.Empty<string>()),
                existingState?.SecurityReview ?? new SecurityReview(Array.Empty<SecurityFinding>(), Array.Empty<string>()),
                Spec: spec),
            RunPhases.PLAN_APPROVAL,
            null,
            cancellationToken).ConfigureAwait(false);

        string planSummary = BuildPlanStepSummary(plan);
        string specMarkdown = spec is not null
            ? $"Task: {spec.Task}\nOutcome: {spec.DesiredOutcome}\nCriteria: {string.Join(", ", spec.AcceptanceCriteria)}"
            : "(no spec generated)";
        string planReviewMarkdown = BuildPlanReviewMarkdown(plan, spec);
        string planHash = ComputePlanHash(plan);

        await this.RecordPlanProposalAsync(request, checkpoint.WorkspaceRoot, runId, spec, planReviewMarkdown, planHash, cancellationToken).ConfigureAwait(false);
        await this.PublishPlanReviewAsync(runId, runDirectory, planReviewMarkdown, planHash, progress, cancellationToken).ConfigureAwait(false);

        PlanApprovalResponse response = await this._approvalBridge.RequestApprovalAsync(
            new PlanApprovalRequest(
                spec ?? new ClarificationSpec(string.Empty, string.Empty, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()),
                plan,
                specMarkdown,
                planSummary,
                planReviewMarkdown,
                ResolvePlanningSessionId(request, runId),
                runId),
            cancellationToken).ConfigureAwait(false);

        PlanApproval approval = new PlanApproval(response.Decision, DateTimeOffset.UtcNow, planHash, response.Reason);
        await this._services.RunInfrastructure.ArtifactWriter.WritePlanApprovalAsync(runDirectory, approval, cancellationToken).ConfigureAwait(false);
        await this._services.RunInfrastructure.EventLogger.AppendEventAsync(runDirectory, new
        {
            runId,
            source = WellKnownSources.ORCHESTRATOR,
            message = $"Plan approval decision: {approval.Decision}",
            decision = approval.Decision,
            reason = approval.Reason
        }, cancellationToken).ConfigureAwait(false);

        PersistedRunState? updatedState = this._services.SessionContext.RunStateStore.GetState(runDirectory);
        await this.WriteRunStateAsync(
            checkpoint,
            new RunProgressSnapshot(
                updatedState?.CompletedStepIds ?? Array.Empty<int>(),
                updatedState?.ReviewIteration ?? 0,
                updatedState?.FrontendPlan ?? string.Empty,
                updatedState?.FilesTouched ?? Array.Empty<string>(),
                updatedState?.Review ?? new ArchitectureReview(Array.Empty<ArchitectureFinding>(), Array.Empty<string>()),
                updatedState?.SecurityReview ?? new SecurityReview(Array.Empty<SecurityFinding>(), Array.Empty<string>()),
                Spec: spec,
                Approval: approval,
                LastBuildOutcome: updatedState?.LastBuildOutcome),
            RunPhases.PLAN_APPROVAL,
            null,
            cancellationToken).ConfigureAwait(false);

        await this.RecordPlanDecisionAsync(request, checkpoint.WorkspaceRoot, runId, approval, cancellationToken).ConfigureAwait(false);

        return new PlanApprovalInteractionResult(approval, response.Attachments);
    }

    private async Task<(ClarificationSpec Spec, IReadOnlyList<ClarificationAnswer> Answers)> RunClarificationLoopAsync(
        RunStateCheckpoint checkpoint,
        IReadOnlyList<ClarificationAnswer> existingAnswers,
        PlanningAgent clarificationAgent,
        IProgress<RuntimeProgressEvent>? progress,
        CancellationToken cancellationToken)
    {
        RunRequest request = checkpoint.Request;
        string workspaceRoot = checkpoint.WorkspaceRoot;
        string runId = checkpoint.RunId;
        List<ClarificationAnswer> clarificationAnswers = existingAnswers.ToList();
        IReadOnlyList<ConversationMessage>? conversationHistory = this.GetPlanningConversationHistory(request, workspaceRoot, runId);
        ClarificationSpec spec = await clarificationAgent.BuildClarificationSpecAsync(
            request,
            workspaceRoot,
            clarificationAnswers,
            conversationHistory: conversationHistory,
            agentId: clarificationAgent.Id,
            agentRole: clarificationAgent.Role,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await this.PersistClarificationStateAsync(runId, checkpoint.RunDirectory, spec, clarificationAnswers, cancellationToken).ConfigureAwait(false);

        if (!IsPlanningWorkflow(request.Workflow) || this._userInputBridge is null)
        {
            return (spec, clarificationAnswers);
        }

        for (int round = 1; round <= MAX_CLARIFICATION_ROUNDS && spec.OpenQuestions.Count > 0; round++)
        {
            ClarificationSpec? updatedSpec = await this.RunClarificationRoundAsync(
                checkpoint,
                spec,
                clarificationAnswers,
                clarificationAgent,
                progress,
                cancellationToken).ConfigureAwait(false);
            if (updatedSpec is null)
            {
                break;
            }

            spec = updatedSpec;
        }

        return (spec, clarificationAnswers);
    }

    private async Task<ClarificationSpec?> RunClarificationRoundAsync(
        RunStateCheckpoint checkpoint,
        ClarificationSpec spec,
        List<ClarificationAnswer> clarificationAnswers,
        PlanningAgent clarificationAgent,
        IProgress<RuntimeProgressEvent>? progress,
        CancellationToken cancellationToken)
    {
        RunRequest request = checkpoint.Request;
        string workspaceRoot = checkpoint.WorkspaceRoot;
        string runId = checkpoint.RunId;
        List<string> unansweredQuestions = spec.OpenQuestions
            .Where(question => clarificationAnswers.All(answer => !string.Equals(answer.Question, question, StringComparison.Ordinal)))
            .ToList();

        if (unansweredQuestions.Count == 0)
        {
            return null;
        }

        progress?.Report(new RuntimeProgressEvent(
            DateTimeOffset.UtcNow,
            WellKnownSources.ORCHESTRATOR,
            "Awaiting planning clarification",
            unansweredQuestions.Count == 1
                ? unansweredQuestions[0]
                : string.Join(Environment.NewLine, unansweredQuestions.Select(question => $"- {question}"))));

        List<UserInputRequest> requests = unansweredQuestions
            .Select(question => new UserInputRequest
            {
                Question = question,
                Choices = new List<string>()
            })
            .ToList();

        foreach (string question in unansweredQuestions)
        {
            await this.AppendPlanningSessionMessageAsync(
                request,
                workspaceRoot,
                runId,
                ArchHarness.App.Storage.PlanningSessionRecorder.CreateMessage(
                    ConversationRoles.ASSISTANT,
                    ConversationMessageKinds.CLARIFICATION_QUESTION,
                    question,
                    authorAgent: clarificationAgent.Role),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<UserInputResponse> responses = unansweredQuestions.Count == 1
            ? new[] { await this._userInputBridge!.RequestInputAsync(requests[0]).ConfigureAwait(false) }
            : await this._userInputBridge!.RequestInputsAsync(requests).ConfigureAwait(false);

        for (int index = 0; index < unansweredQuestions.Count; index++)
        {
            UserInputResponse response = index < responses.Count
                ? responses[index]
                : new UserInputResponse { Answer = string.Empty, WasFreeform = true };
            clarificationAnswers.Add(new ClarificationAnswer(unansweredQuestions[index], response.Answer ?? string.Empty));
            await this.AppendPlanningSessionMessageAsync(
                request,
                workspaceRoot,
                runId,
                ArchHarness.App.Storage.PlanningSessionRecorder.CreateMessage(
                    ConversationRoles.USER,
                    ConversationMessageKinds.CLARIFICATION_ANSWER,
                    $"Q: {unansweredQuestions[index]}{Environment.NewLine}A: {response.Answer ?? string.Empty}"),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        await this.PersistClarificationStateAsync(runId, checkpoint.RunDirectory, spec, clarificationAnswers, cancellationToken).ConfigureAwait(false);

        progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, WellKnownSources.ORCHESTRATOR, "Regenerating clarification spec"));
        ClarificationSpec updatedSpec = await clarificationAgent.BuildClarificationSpecAsync(
            request,
            workspaceRoot,
            clarificationAnswers,
            conversationHistory: this.GetPlanningConversationHistory(request, workspaceRoot, runId),
            agentId: clarificationAgent.Id,
            agentRole: clarificationAgent.Role,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await this.PersistClarificationStateAsync(runId, checkpoint.RunDirectory, updatedSpec, clarificationAnswers, cancellationToken).ConfigureAwait(false);
        return updatedSpec;
    }

    private async Task<ExecutionPlan> RunPlanningApprovalLoopAsync(
        RunStateCheckpoint checkpoint,
        IWorkspaceAdapter adapter,
        ExecutionPlan plan,
        ClarificationSpec? spec,
        IReadOnlyList<ClarificationAnswer> clarificationAnswers,
        IProgress<RuntimeProgressEvent>? progress,
        CancellationToken cancellationToken)
    {
        RunRequest request = checkpoint.Request;
        while (true)
        {
            PlanApprovalInteractionResult? approvalResult = await this.RequestPlanApprovalAsync(request, spec, plan, checkpoint.RunId, checkpoint.RunDirectory, progress, cancellationToken).ConfigureAwait(false);
            if (approvalResult is null || string.Equals(approvalResult.Approval.Decision, PlanApprovalDecisions.CANCELED, StringComparison.OrdinalIgnoreCase))
            {
                throw new OperationCanceledException("Plan approval was canceled by user.");
            }

            PlanApproval approval = approvalResult.Approval;

            if (!string.Equals(approval.Decision, PlanApprovalDecisions.REGENERATE, StringComparison.OrdinalIgnoreCase))
            {
                return plan;
            }

            progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, WellKnownSources.ORCHESTRATOR, "Regenerating execution plan", approval.Reason));
            ConversationMessage revisionMessage = ArchHarness.App.Storage.PlanningSessionRecorder.CreateMessage(
                ConversationRoles.USER,
                ConversationMessageKinds.PLAN_REVISION,
                approval.Reason ?? string.Empty,
                approvalResult.Attachments);

            await this.AppendPlanningSessionMessageAsync(
                request,
                adapter.RootPath,
                checkpoint.RunId,
                revisionMessage,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            IReadOnlyList<ConversationMessage> followUpPromptHistory = this.BuildPlanRegenerationPromptHistory(
                request,
                adapter.RootPath,
                checkpoint.RunId,
                revisionMessage);

            plan = await this._services.RunPhases.PlanExecutor.BuildPlanAsync(
                request,
                adapter,
                checkpoint.RunId,
                checkpoint.RunDirectory,
                new PlanningContext(
                    spec,
                    clarificationAnswers,
                    PlanRevisionRequest: null,
                    ConversationHistory: followUpPromptHistory,
                    PlanningSessionId: ResolvePlanningSessionId(request, checkpoint.RunId),
                    Attachments: approvalResult.Attachments,
                    UseFollowUpOnlyPrompt: true),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private PlanningAgent ResolveInitialPlanner()
        => this._planningServices.PlanningAgent;

    private async Task PersistClarificationStateAsync(
        string runId,
        string runDirectory,
        ClarificationSpec spec,
        IReadOnlyList<ClarificationAnswer> clarificationAnswers,
        CancellationToken cancellationToken)
    {
        await this._services.RunInfrastructure.ArtifactWriter.WriteClarificationSpecAsync(runDirectory, spec, cancellationToken).ConfigureAwait(false);
        await this._services.RunInfrastructure.EventLogger.AppendEventAsync(
            runDirectory,
            new
            {
                runId,
                source = WellKnownSources.ORCHESTRATOR,
                message = "Clarification spec generated",
                clarificationAnswerCount = clarificationAnswers.Count
            },
            cancellationToken).ConfigureAwait(false);

        PersistedRunState? existingState = this._services.SessionContext.RunStateStore.GetState(runDirectory)
            ?? throw new InvalidOperationException("Cannot persist clarification state: no run state found.");
        RunStateCheckpoint checkpoint = new(runId, runDirectory, existingState.WorkspaceRoot, existingState.Request);
        await this.WriteRunStateAsync(
            checkpoint,
            new RunProgressSnapshot(
                existingState.CompletedStepIds,
                existingState.ReviewIteration,
                existingState.FrontendPlan,
                existingState.FilesTouched,
                existingState.Review,
                existingState.SecurityReview,
                Spec: spec,
                Approval: existingState.Approval,
                LastBuildOutcome: existingState.LastBuildOutcome,
                ClarificationAnswers: clarificationAnswers),
            RunPhases.CLARIFICATION,
            null,
            cancellationToken,
            existingState.Status).ConfigureAwait(false);
    }

    private async Task<(ExecutionPlan Plan, ClarificationSpec? Spec, IReadOnlyList<ClarificationAnswer> ClarificationAnswers)> LoadPlanningSourcePlanAsync(
        RunRequest request,
        IWorkspaceAdapter adapter,
        string runId,
        string runDirectory,
        CancellationToken cancellationToken)
    {
        string planningRunId = request.PlanningSourceRunId
            ?? throw new InvalidOperationException("Cannot seed implementation without a planning source run id.");
        string planningRunDirectory = Path.Combine(FileSystemStorageHelper.GetRunsRootPath(adapter.RootPath), planningRunId);
        PersistedRunState planningState = this._services.SessionContext.RunStateStore.GetState(planningRunDirectory)
            ?? throw new InvalidOperationException($"Planning run '{planningRunId}' could not be found.");

        if (!IsPlanningWorkflow(planningState.Request.Workflow))
        {
            throw new InvalidOperationException($"Run '{planningRunId}' is not a planning run.");
        }

        if (!string.Equals(planningState.Phase, RunPhases.HANDOFF_READY, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(planningState.Status, RunStatuses.COMPLETED, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Planning run '{planningRunId}' is not ready for implementation handoff.");
        }

        string executionPlanPath = FileSystemStorageHelper.GetRunFilePath(planningRunDirectory, "ExecutionPlan.json");
        if (!File.Exists(executionPlanPath))
        {
            throw new InvalidOperationException($"Planning run '{planningRunId}' does not have a persisted execution plan.");
        }

        ExecutionPlan plan = JsonSerializer.Deserialize<ExecutionPlan>(
                await File.ReadAllTextAsync(executionPlanPath, cancellationToken).ConfigureAwait(false),
                JsonDefaults.INDENTED)
            ?? throw new InvalidOperationException($"Unable to deserialize the execution plan for planning run '{planningRunId}'.");

        await this._services.RunInfrastructure.ArtifactWriter.WriteExecutionPlanAsync(runDirectory, plan, cancellationToken).ConfigureAwait(false);
        if (planningState.Spec is not null)
        {
            await this._services.RunInfrastructure.ArtifactWriter.WriteClarificationSpecAsync(runDirectory, planningState.Spec, cancellationToken).ConfigureAwait(false);
        }

        if (planningState.Approval is not null)
        {
            await this._services.RunInfrastructure.ArtifactWriter.WritePlanApprovalAsync(runDirectory, planningState.Approval, cancellationToken).ConfigureAwait(false);
        }

        await this._services.RunInfrastructure.EventLogger.AppendEventAsync(
            runDirectory,
            new
            {
                runId,
                source = WellKnownSources.ORCHESTRATOR,
                message = $"Seeded implementation from planning run {planningRunId}",
                planningSourceRunId = planningRunId
            },
            cancellationToken).ConfigureAwait(false);

        return (plan, planningState.Spec, planningState.ClarificationAnswers ?? Array.Empty<ClarificationAnswer>());
    }

    private async Task FinalizePlanningRunAsync(
        RunStateCheckpoint checkpoint,
        RunRequest request,
        ExecutionPlan plan,
        ClarificationSpec? spec,
        IReadOnlyList<ClarificationAnswer> clarificationAnswers,
        IProgress<RuntimeProgressEvent>? progress,
        CancellationToken cancellationToken)
    {
        ArchitectureReview emptyArchitectureReview = new(Array.Empty<ArchitectureFinding>(), Array.Empty<string>());
        SecurityReview emptySecurityReview = new(Array.Empty<SecurityFinding>(), Array.Empty<string>());

        await this._services.RunInfrastructure.ArtifactWriter.WriteFinalSummaryAsync(
            checkpoint.RunDirectory,
            BuildPlanningSummary(plan, spec),
            cancellationToken).ConfigureAwait(false);

        string[] modelOverrides = request.ModelOverrides?.Select(pair => $"{pair.Key}={pair.Value}").ToArray() ?? Array.Empty<string>();
        IReadOnlyList<CopilotModelUsage> usage = this._services.SessionContext.CopilotClient.GetUsageSnapshot();
        IReadOnlyList<object> agentModelUsage = this._services.AgentModelUsageBuilder.Build(request.ModelOverrides);

        await this._services.RunInfrastructure.ArtifactWriter.WriteRunLogAsync(checkpoint.RunDirectory, new
        {
            status = RunStatuses.COMPLETED,
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

        await this._services.RunInfrastructure.EventLogger.AppendEventAsync(
            checkpoint.RunDirectory,
            new { runId = checkpoint.RunId, source = WellKnownSources.ORCHESTRATOR, message = PLANNING_COMPLETED_MESSAGE },
            cancellationToken).ConfigureAwait(false);

        await this.WriteRunStateAsync(
            checkpoint,
            new RunProgressSnapshot(
                plan.Steps.Select(step => step.Id).ToArray(),
                0,
                string.Empty,
                Array.Empty<string>(),
                emptyArchitectureReview,
                emptySecurityReview,
                Spec: spec,
                Approval: this._services.SessionContext.RunStateStore.GetState(checkpoint.RunDirectory)?.Approval,
                ClarificationAnswers: clarificationAnswers),
            RunPhases.HANDOFF_READY,
            null,
            cancellationToken,
            RunStatuses.COMPLETED).ConfigureAwait(false);
        progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, WellKnownSources.ORCHESTRATOR, PLANNING_COMPLETED_MESSAGE));
        await this.AppendPlanningSessionMessageAsync(
            request,
            checkpoint.WorkspaceRoot,
            checkpoint.RunId,
            ArchHarness.App.Storage.PlanningSessionRecorder.CreateMessage(
                ConversationRoles.SYSTEM,
                ConversationMessageKinds.HANDOFF,
                "Planning complete; ready for implementation handoff.",
                authorAgent: WellKnownSources.ORCHESTRATOR),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task<ReplanningRunState> RunReviewReplanningCyclesAsync(
        ReplanningScope scope,
        ReplanningRunState state,
        CancellationToken cancellationToken)
    {
        for (int cycle = 0; cycle <= MAX_ORCHESTRATOR_REPLANNING_CYCLES; cycle++)
        {
            (ArchitectureReview review, SecurityReview securityReview, IReadOnlyList<string> filesTouched) = await this.RunArchitectureLoopAsync(
                state,
                scope,
                cancellationToken).ConfigureAwait(false);

            state = state with { Review = review, SecurityReview = securityReview, FilesTouched = filesTouched };

            IReadOnlyList<StepFollowUpHint> hints = ReplanningSignalBuilder.BuildReviewHints(
                state.Review,
                state.SecurityReview,
                state.FilesTouched,
                state.Plan.Steps.LastOrDefault(s => s.Agent == AgentNames.ARCHITECTURE)?.Languages,
                state.Plan.Steps.LastOrDefault(s => s.Agent == AgentNames.SECURITY)?.Languages);

            if (hints.Count == 0)
            {
                return state;
            }

            if (cycle >= MAX_ORCHESTRATOR_REPLANNING_CYCLES)
            {
                await this.AppendReplanningStoppedEventAsync(
                    scope.Checkpoint.RunDirectory,
                    scope.Checkpoint.RunId,
                    "review",
                    $"Review replanning stopped after {MAX_ORCHESTRATOR_REPLANNING_CYCLES} cycle(s) with unresolved review signals.",
                    cancellationToken).ConfigureAwait(false);
                return state;
            }

            ReplanningWaveExecution replanning = await this.ExecuteReplanningWaveAsync(
                scope,
                state,
                hints,
                source: "review",
                cycle: cycle + 1,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!replanning.Executed)
            {
                return state;
            }

            state = replanning.State;
            scope = scope with { Context = scope.Context with { ResumeState = this._services.SessionContext.RunStateStore.GetState(scope.Checkpoint.RunDirectory) } };
        }

        return state;
    }

    private async Task<ReplanningWaveExecution> ExecuteReplanningWaveAsync(
        ReplanningScope scope,
        ReplanningRunState state,
        IReadOnlyList<StepFollowUpHint> hints,
        string source,
        int cycle,
        CancellationToken cancellationToken)
    {
        if (hints.Count == 0)
        {
            return ReplanningWaveExecution.NotExecuted(state);
        }

        int nextWave = state.Plan.Steps.Count > 0 ? state.Plan.Steps.Max(step => step.Wave) + 1 : 1;
        int nextStepId = state.Plan.Steps.Count > 0 ? state.Plan.Steps.Max(step => step.Id) + 1 : 1;
        StepOutcome syntheticOutcome = new(
            StepId: 0,
            Agent: WellKnownSources.ORCHESTRATOR,
            FilesTouchedDelta: Array.Empty<string>(),
            CompletionStatus: StepCompletionStatuses.PARTIAL,
            UnresolvedWork: hints.Select(hint => hint.Objective).ToArray(),
            FollowUpHints: hints);

        ContinuationPlanningResult planningResult = this._planningServices.ContinuationPlanner.PlanNextWave(
            new ContinuationPlanningContext(
                state.Plan,
                new[] { syntheticOutcome },
                new[] { syntheticOutcome },
                state.FilesTouched,
                state.FilesTouched.Count,
                nextWave,
                nextStepId));

        if (planningResult.NewSteps.Count == 0)
        {
            await this._services.RunInfrastructure.EventLogger.AppendEventAsync(
                scope.Checkpoint.RunDirectory,
                new
                {
                    runId = scope.Checkpoint.RunId,
                    source = WellKnownSources.ORCHESTRATOR,
                    message = $"Orchestrator produced no {source} replanning steps ({planningResult.Reason}).",
                    replanningSource = source,
                    cycle
                },
                cancellationToken).ConfigureAwait(false);
            return ReplanningWaveExecution.NotExecuted(state);
        }

        ExecutionPlan appendedPlan = state.Plan with { Steps = state.Plan.Steps.Concat(planningResult.NewSteps).ToArray() };
        await this._services.RunInfrastructure.ArtifactWriter.WriteExecutionPlanAsync(scope.Checkpoint.RunDirectory, appendedPlan, cancellationToken).ConfigureAwait(false);
        await this._services.RunInfrastructure.EventLogger.AppendEventAsync(
            scope.Checkpoint.RunDirectory,
            new
            {
                runId = scope.Checkpoint.RunId,
                source = WellKnownSources.ORCHESTRATOR,
                message = $"Orchestrator appended {planningResult.NewSteps.Count} {source} remediation step(s) as wave {nextWave}.",
                replanningSource = source,
                cycle,
                wave = nextWave,
                stepIds = planningResult.NewSteps.Select(step => step.Id).ToArray(),
                reason = planningResult.Reason
            },
            cancellationToken).ConfigureAwait(false);
        scope.Progress?.Report(new RuntimeProgressEvent(
            DateTimeOffset.UtcNow,
            WellKnownSources.ORCHESTRATOR,
            $"Planning {source} remediation wave",
            string.Join(Environment.NewLine, planningResult.NewSteps.Select(step => $"- {step.Agent}: {step.Objective}"))));

        PersistedRunState? existingState = this._services.SessionContext.RunStateStore.GetState(scope.Checkpoint.RunDirectory);
        PersistedRunState resumeState = new(
            scope.Checkpoint.RunId,
            scope.Checkpoint.RunDirectory,
            scope.Checkpoint.WorkspaceRoot,
            RunStatuses.RUNNING,
            RunPhases.EXECUTING_PLAN,
            existingState?.StartedAtUtc ?? DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            scope.Context.Request,
            state.Plan.Steps.Select(step => step.Id).ToArray(),
            existingState?.ReviewIteration ?? 0,
            state.FrontendPlan,
            state.FilesTouched.ToArray(),
            state.Review,
            state.SecurityReview,
            Spec: scope.Spec,
            Approval: existingState?.Approval,
            LastBuildOutcome: state.LastBuildOutcome,
            ClarificationAnswers: scope.ClarificationAnswers.ToArray(),
            PlanningSessionId: ResolvePlanningSessionId(scope.Context.Request, scope.Checkpoint.RunId),
            CurrentWave: nextWave);

        PlanExecutionResult executionResult = await this._services.RunPhases.PlanExecutor.ExecuteApprovedPlanAsync(
            appendedPlan,
            scope.Context.Request,
            scope.Adapter,
            new StepExecutionContext(scope.Checkpoint.RunId, scope.Checkpoint.RunDirectory, resumeState),
            scope.Progress,
            cancellationToken).ConfigureAwait(false);

        return new ReplanningWaveExecution(
            true,
            new ReplanningRunState(
                appendedPlan,
                executionResult.StepResult.FrontendPlan,
                executionResult.StepResult.FilesTouched,
                executionResult.StepResult.Review,
                executionResult.StepResult.SecurityReview,
                executionResult.StepResult.LastBuildOutcome ?? state.LastBuildOutcome));
    }

    private async Task AppendReplanningStoppedEventAsync(
        string runDirectory,
        string runId,
        string source,
        string message,
        CancellationToken cancellationToken)
    {
        await this._services.RunInfrastructure.EventLogger.AppendEventAsync(
            runDirectory,
            new
            {
                runId,
                source = WellKnownSources.ORCHESTRATOR,
                status = RunEventStatuses.BLOCKED,
                replanningSource = source,
                message
            },
            cancellationToken).ConfigureAwait(false);
    }

    private sealed record ReplanningScope(
        OrchestratedRunContext Context,
        RunStateCheckpoint Checkpoint,
        IWorkspaceAdapter Adapter,
        IProgress<RuntimeProgressEvent>? Progress,
        ClarificationSpec? Spec,
        IReadOnlyList<ClarificationAnswer> ClarificationAnswers);

    private sealed record ReplanningRunState(
        ExecutionPlan Plan,
        string FrontendPlan,
        IReadOnlyList<string> FilesTouched,
        ArchitectureReview Review,
        SecurityReview SecurityReview,
        BuildOutcome? LastBuildOutcome);

    private sealed record ReplanningWaveExecution(
        bool Executed,
        ReplanningRunState State)
    {
        public static ReplanningWaveExecution NotExecuted(ReplanningRunState state)
            => new(false, state);
    }

    private async Task<(ArchitectureReview Review, SecurityReview SecurityReview, IReadOnlyList<string> FilesTouched)> RunArchitectureLoopAsync(
        ReplanningRunState state,
        ReplanningScope scope,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string>? architectureLanguages = state.Plan.Steps.LastOrDefault(s => s.Agent == "Architecture")?.Languages;
        IReadOnlyList<string>? securityLanguages = state.Plan.Steps.LastOrDefault(s => s.Agent == "Security")?.Languages;
        PersistedRunState? resumeState = scope.Context.ResumeState;
        ArchitectureReview review = state.Review;
        SecurityReview securityReview = state.SecurityReview;
        IReadOnlyList<string> filesTouched = state.FilesTouched;

        (review, securityReview, filesTouched) = await this._services.RunPhases.ArchitectureReviewLoop.RunAsync(
            new ArchitectureLoopRequest(
                state.Plan.IterationStrategy,
                review,
                securityReview,
                filesTouched,
                architectureLanguages,
                securityLanguages,
                scope.Context.Request,
                resumeState?.ReviewIteration ?? 0),
            scope.Adapter,
            scope.Progress,
            cancellationToken).ConfigureAwait(false);

        if (review.RequiredActions.Contains(ArchitectureReviewLoop.NO_PROGRESS_BLOCKED_STATUS, StringComparer.OrdinalIgnoreCase)
            || securityReview.RequiredActions.Contains(ArchitectureReviewLoop.NO_PROGRESS_BLOCKED_STATUS, StringComparer.OrdinalIgnoreCase))
        {
            await this._services.RunInfrastructure.EventLogger.AppendEventAsync(
                scope.Checkpoint.RunDirectory,
                new
                {
                    runId = scope.Checkpoint.RunId,
                    source = WellKnownSources.ARCHITECTURE_LOOP,
                    status = RunEventStatuses.BLOCKED,
                    message = "Architecture review iterations produced identical findings; loop stopped early."
                },
                cancellationToken).ConfigureAwait(false);
        }

        return (review, securityReview, filesTouched);
    }

    private async Task FinalizeRunAsync(
        ReplanningScope scope,
        ReplanningRunState state,
        int reviewIteration,
        CancellationToken cancellationToken)
    {
        RunStateCheckpoint checkpoint = scope.Checkpoint;
        RunRequest request = scope.Context.Request;
        IWorkspaceAdapter adapter = scope.Adapter;
        ExecutionPlan plan = state.Plan;
        string frontendPlan = state.FrontendPlan;
        IReadOnlyList<string> filesTouched = state.FilesTouched;
        ArchitectureReview review = state.Review;
        SecurityReview securityReview = state.SecurityReview;
        BuildOutcome? lastBuildOutcome = state.LastBuildOutcome;

        await this._services.RunInfrastructure.ArtifactWriter.WriteArchitectureReviewAsync(checkpoint.RunDirectory, review, cancellationToken).ConfigureAwait(false);
        await this._services.RunInfrastructure.ArtifactWriter.WriteSecurityReviewAsync(checkpoint.RunDirectory, securityReview, cancellationToken).ConfigureAwait(false);
        await this.WriteRunStateAsync(
            checkpoint,
            new RunProgressSnapshot(plan.Steps.Select(step => step.Id).ToArray(), reviewIteration, frontendPlan, filesTouched, review, securityReview, Spec: scope.Spec, LastBuildOutcome: lastBuildOutcome, ClarificationAnswers: scope.ClarificationAnswers),
            RunPhases.FINALIZING,
            null,
            cancellationToken).ConfigureAwait(false);

        CompletionValidationResult? validationResult = null;
        for (int cycle = 0; cycle <= MAX_ORCHESTRATOR_REPLANNING_CYCLES; cycle++)
        {
            VerificationWorkflowResult verificationResult = await this._planningServices.VerificationWorkflow.RunAsync(
                new RunVerificationRequest(request, checkpoint.RunDirectory, plan, review, securityReview, scope.Spec, lastBuildOutcome, filesTouched),
                adapter,
                scope.Progress,
                cancellationToken).ConfigureAwait(false);
            lastBuildOutcome = verificationResult.LastBuildOutcome;
            filesTouched = verificationResult.FilesTouched;
            validationResult = verificationResult.ValidationResult;

            if (validationResult.Passed)
            {
                break;
            }

            IReadOnlyList<StepFollowUpHint> hints = ReplanningSignalBuilder.BuildVerificationHints(
                validationResult,
                scope.Spec,
                plan,
                lastBuildOutcome,
                filesTouched);
            if (hints.Count == 0)
            {
                break;
            }

            if (cycle >= MAX_ORCHESTRATOR_REPLANNING_CYCLES)
            {
                await this.AppendReplanningStoppedEventAsync(
                    checkpoint.RunDirectory,
                    checkpoint.RunId,
                    "verification",
                    $"Verification replanning stopped after {MAX_ORCHESTRATOR_REPLANNING_CYCLES} cycle(s) with unmet completion criteria.",
                    cancellationToken).ConfigureAwait(false);
                break;
            }

            ReplanningWaveExecution replanning = await this.ExecuteReplanningWaveAsync(
                new ReplanningScope(
                    new OrchestratedRunContext(adapter, request, this._services.SessionContext.RunStateStore.GetState(checkpoint.RunDirectory), null),
                    checkpoint,
                    adapter,
                    scope.Progress,
                    scope.Spec,
                    scope.ClarificationAnswers),
                new ReplanningRunState(plan, frontendPlan, filesTouched, review, securityReview, lastBuildOutcome),
                hints,
                source: "verification",
                cycle: cycle + 1,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!replanning.Executed)
            {
                break;
            }

            plan = replanning.State.Plan;
            frontendPlan = replanning.State.FrontendPlan;
            filesTouched = replanning.State.FilesTouched;
            review = replanning.State.Review;
            securityReview = replanning.State.SecurityReview;
            lastBuildOutcome = replanning.State.LastBuildOutcome;

            (review, securityReview, filesTouched) = await this.RunArchitectureLoopAsync(
                new ReplanningRunState(plan, frontendPlan, filesTouched, review, securityReview, lastBuildOutcome),
                new ReplanningScope(
                    new OrchestratedRunContext(adapter, request, this._services.SessionContext.RunStateStore.GetState(checkpoint.RunDirectory), null),
                    checkpoint,
                    adapter,
                    scope.Progress,
                    scope.Spec,
                    scope.ClarificationAnswers),
                cancellationToken).ConfigureAwait(false);

            await this._services.RunInfrastructure.ArtifactWriter.WriteArchitectureReviewAsync(checkpoint.RunDirectory, review, cancellationToken).ConfigureAwait(false);
            await this._services.RunInfrastructure.ArtifactWriter.WriteSecurityReviewAsync(checkpoint.RunDirectory, securityReview, cancellationToken).ConfigureAwait(false);
            await this.WriteRunStateAsync(
                checkpoint,
                new RunProgressSnapshot(plan.Steps.Select(step => step.Id).ToArray(), reviewIteration, frontendPlan, filesTouched, review, securityReview, Spec: scope.Spec, LastBuildOutcome: lastBuildOutcome, ClarificationAnswers: scope.ClarificationAnswers),
                RunPhases.FINALIZING,
                null,
                cancellationToken).ConfigureAwait(false);
        }

        validationResult ??= new CompletionValidationResult(false, Array.Empty<CriterionResult>(), "Verification did not run.");
        bool completed = validationResult.Passed;
        await this._services.RunInfrastructure.ArtifactWriter.WriteCompletionValidationAsync(checkpoint.RunDirectory, validationResult, cancellationToken).ConfigureAwait(false);
        await this._services.RunInfrastructure.ArtifactWriter.WriteFinalSummaryAsync(
            checkpoint.RunDirectory,
            BuildFinalSummary(frontendPlan, filesTouched, review, securityReview, validationResult),
            cancellationToken).ConfigureAwait(false);

        string[] modelOverrides = request.ModelOverrides?.Select(pair => $"{pair.Key}={pair.Value}").ToArray() ?? Array.Empty<string>();
        IReadOnlyList<CopilotModelUsage> usage = this._services.SessionContext.CopilotClient.GetUsageSnapshot();
        object[] agentModelUsage = this._services.AgentModelUsageBuilder.Build(request.ModelOverrides);

        await this._services.RunInfrastructure.ArtifactWriter.WriteRunLogAsync(checkpoint.RunDirectory, new
        {
            status = completed ? RunStatuses.COMPLETED : RunStatuses.INCOMPLETE,
            projectId = request.ProjectId,
            projectName = request.ProjectName,
            runTitle = request.RunTitle,
            request.WorkspaceMode,
            request.Workflow,
            request.PermissionHandlerMode,
            modelOverrides,
            agents = agentModelUsage,
            copilotUsage = usage,
            completionValidation = validationResult
        }, cancellationToken).ConfigureAwait(false);

        await this._services.RunInfrastructure.EventLogger.AppendEventAsync(checkpoint.RunDirectory, new { runId = checkpoint.RunId, source = WellKnownSources.ORCHESTRATOR, message = RUN_COMPLETED_MESSAGE }, cancellationToken).ConfigureAwait(false);
        await this.WriteRunStateAsync(
            checkpoint,
            new RunProgressSnapshot(plan.Steps.Select(step => step.Id).ToArray(), reviewIteration, frontendPlan, filesTouched, review, securityReview, Spec: scope.Spec, LastBuildOutcome: lastBuildOutcome, CompletionValidation: validationResult, ClarificationAnswers: scope.ClarificationAnswers),
            completed ? RunTerminalPhases.COMPLETED : RunTerminalPhases.INCOMPLETE,
            null,
            cancellationToken,
            completed ? RunStatuses.COMPLETED : RunStatuses.INCOMPLETE).ConfigureAwait(false);
        scope.Progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, WellKnownSources.ORCHESTRATOR, RUN_COMPLETED_MESSAGE));
    }

    private async Task WriteTerminalRunStateAsync(string runDirectory, string status, string phase, string failureMessage)
    {
        await this._services.SessionContext.RunStateStore.UpdateStateAsync(
            runDirectory,
            existingState => existingState is null
                ? null
                : existingState with
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
        return this._services.SessionContext.RunStateStore.UpdateStateAsync(
            checkpoint.RunDirectory,
            existingState => new PersistedRunState(
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
                failureMessage,
                Spec: progress.Spec ?? existingState?.Spec,
                Approval: progress.Approval ?? existingState?.Approval,
                LastBuildOutcome: progress.LastBuildOutcome ?? existingState?.LastBuildOutcome,
                CompletionValidation: progress.CompletionValidation ?? existingState?.CompletionValidation,
                ClarificationAnswers: progress.ClarificationAnswers?.ToArray() ?? existingState?.ClarificationAnswers,
                HandoffRunId: existingState?.HandoffRunId,
                PlanningSessionId: existingState?.PlanningSessionId ?? (IsPlanningWorkflow(checkpoint.Request.Workflow) ? ResolvePlanningSessionId(checkpoint.Request, checkpoint.RunId) : checkpoint.Request.PlanningSessionId)),
            cancellationToken);
    }

    private static string BuildFinalSummary(
        string frontendPlan,
        IReadOnlyList<string> filesTouched,
        ArchitectureReview review,
        SecurityReview securityReview,
        CompletionValidationResult validationResult)
    {
        int securityHighCount = securityReview.Findings.Count(f => string.Equals(f.Severity, "high", StringComparison.OrdinalIgnoreCase));
        int architectureHighCount = review.Findings.Count(f => string.Equals(f.Severity, "high", StringComparison.OrdinalIgnoreCase));
        string filesTouchedList = string.Join(", ", filesTouched);
        string failedCriteria = validationResult.CriterionResults.Count == 0
            ? NONE_LABEL
            : string.Join(", ", validationResult.CriterionResults.Where(result => !result.Passed).Select(result => result.Criterion));
        return $"""
            # Final Summary
            - Completed: {validationResult.Passed}
            - MateriallyImplemented: {validationResult.Assessment?.MateriallyImplemented}
            - FrontendPlan: {frontendPlan}
            - FilesTouched: {filesTouchedList}
            - SecurityHighFindings: {securityHighCount}
            - ArchitectureHighFindings: {architectureHighCount}
            - FailedCriteria: {failedCriteria}
            """;
    }

    private static string BuildPlanningSummary(ExecutionPlan plan, ClarificationSpec? spec)
    {
        string criteria = spec is null || spec.AcceptanceCriteria.Count == 0
            ? NONE_LABEL
            : string.Join(", ", spec.AcceptanceCriteria);
        string stepList = plan.Steps.Count == 0
            ? NONE_LABEL
            : string.Join(Environment.NewLine, plan.Steps.Select(step => $"- {step.Id}. [{step.Agent}] {step.Objective}"));

        return $"""
            # Planning Summary
            - Task: {spec?.Task ?? "(no clarified task)"}
            - DesiredOutcome: {spec?.DesiredOutcome ?? "(no clarified outcome)"}
            - AcceptanceCriteria: {criteria}
            - Steps:
            {stepList}
            """;
    }

    private static string BuildPlanStepSummary(ExecutionPlan plan)
        => plan.Steps.Count == 0
            ? "(no steps)"
            : string.Join(Environment.NewLine, plan.Steps.Select(s => $"  {s.Id}. [{s.Agent}] {s.Objective}"));

    private static string BuildPlanReviewMarkdown(ExecutionPlan plan, ClarificationSpec? spec)
    {
        string title = string.IsNullOrWhiteSpace(spec?.Task) ? "Planning Review" : spec!.Task.Trim();
        string desiredOutcome = string.IsNullOrWhiteSpace(spec?.DesiredOutcome) ? "Review the proposed execution plan before implementation." : spec!.DesiredOutcome.Trim();
        string steps = plan.Steps.Count == 0
            ? "- No execution steps were produced."
            : string.Join(Environment.NewLine, plan.Steps.Select(step => $"{step.Id}. [{step.Agent}] {step.Objective}"));
        string files = spec?.LikelyTouchpoints is { Count: > 0 }
            ? string.Join(Environment.NewLine, spec.LikelyTouchpoints.Select(path => $"- {path}"))
            : "- To be confirmed during implementation.";
        string verification;
        if (spec?.VerificationCommands is { Count: > 0 })
        {
            verification = string.Join(Environment.NewLine, spec.VerificationCommands.Select((command, index) => $"{index + 1}. {command.Name}: {command.Command}"));
        }
        else if (plan.CompletionCriteria.Count > 0)
        {
            verification = string.Join(Environment.NewLine, plan.CompletionCriteria.Select((criterion, index) => $"{index + 1}. {criterion}"));
        }
        else
        {
            verification = "1. Confirm the implementation satisfies the approved plan.";
        }
        string decisions = spec?.DecisionNotes is { Count: > 0 }
            ? string.Join(Environment.NewLine, spec.DecisionNotes.Select(note => $"- {note}"))
            : "- No explicit decisions recorded yet.";

        return $"""
            ## Plan: {title}

            {desiredOutcome}

            **Steps**
            {steps}

            **Relevant files**
            {files}

            **Verification**
            {verification}

            **Decisions**
            {decisions}
            """;
    }

    private static string ComputePlanHash(ExecutionPlan plan)
    {
        string serialized = JsonSerializer.Serialize(plan, JsonDefaults.WEB_INDENTED);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(serialized));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ResolvePlanningSessionId(RunRequest request, string runId)
        => string.IsNullOrWhiteSpace(request.PlanningSessionId) ? runId : request.PlanningSessionId!;

    private IReadOnlyList<ConversationMessage>? GetPlanningConversationHistory(RunRequest request, string workspaceRoot, string runId)
    {
        if (this._planningSessionRecorder is null)
        {
            return null;
        }

        string sessionId = ResolvePlanningSessionId(request, runId);
        try
        {
            return this._planningSessionRecorder.Get(workspaceRoot, sessionId)?.Messages;
        }
        catch
        {
            return null;
        }
    }

    private async Task RecordPlanProposalAsync(
        RunRequest request,
        string workspaceRoot,
        string runId,
        ClarificationSpec? spec,
        string planReviewMarkdown,
        string planHash,
        CancellationToken cancellationToken)
    {
        await this.AppendPlanningSessionMessageAsync(
            request,
            workspaceRoot,
            runId,
            ArchHarness.App.Storage.PlanningSessionRecorder.CreateMessage(
                ConversationRoles.ASSISTANT,
                ConversationMessageKinds.PLAN_PROPOSAL,
                planReviewMarkdown,
                authorAgent: WellKnownSources.ORCHESTRATOR),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (this._planningSessionRecorder is null)
        {
            return;
        }

        try
        {
            await this._planningSessionRecorder.UpdateArtifactsAsync(
                workspaceRoot,
                ResolvePlanningSessionId(request, runId),
                spec,
                approval: null,
                currentPlanHash: planHash,
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort.
        }
    }

    private async Task RecordPlanDecisionAsync(
        RunRequest request,
        string workspaceRoot,
        string runId,
        PlanApproval approval,
        CancellationToken cancellationToken)
    {
        string text = approval.Decision;
        if (!string.Equals(approval.Decision, PlanApprovalDecisions.REGENERATE, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(approval.Reason))
        {
            text = $"{approval.Decision}: {approval.Reason}";
        }
        await this.AppendPlanningSessionMessageAsync(
            request,
            workspaceRoot,
            runId,
            ArchHarness.App.Storage.PlanningSessionRecorder.CreateMessage(
                ConversationRoles.USER,
                ConversationMessageKinds.PLAN_DECISION,
                text),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (this._planningSessionRecorder is null)
        {
            return;
        }

        try
        {
            await this._planningSessionRecorder.UpdateArtifactsAsync(
                workspaceRoot,
                ResolvePlanningSessionId(request, runId),
                spec: null,
                approval,
                currentPlanHash: approval.PlanHash,
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort.
        }
    }

    private IReadOnlyList<ConversationMessage> BuildPlanRegenerationPromptHistory(
        RunRequest request,
        string workspaceRoot,
        string runId,
        ConversationMessage revisionMessage)
    {
        ConversationMessage? decisionMessage = this.GetPlanningConversationHistory(request, workspaceRoot, runId)?
            .LastOrDefault(message => string.Equals(message.Kind, ConversationMessageKinds.PLAN_DECISION, StringComparison.Ordinal));

        return decisionMessage is null
            ? new[] { revisionMessage }
            : new[] { decisionMessage, revisionMessage };
    }

    private async Task PublishPlanReviewAsync(
        string runId,
        string runDirectory,
        string planReviewMarkdown,
        string planHash,
        IProgress<RuntimeProgressEvent>? progress,
        CancellationToken cancellationToken)
    {
        string planReviewAgentId = BuildPlanReviewAgentId(runId, planHash);
        progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, "Planning", "Plan review ready", planReviewMarkdown, planReviewAgentId));
        await this._services.RunInfrastructure.EventLogger.AppendEventAsync(runDirectory, new
        {
            runId,
            kind = "agent-delta",
            source = WellKnownSources.ORCHESTRATOR,
            agentId = planReviewAgentId,
            agentRole = "Planning",
            message = planReviewMarkdown,
            contentFormat = "markdown",
            streamKind = "assistant",
            title = "Plan Review",
            timestampUtc = DateTimeOffset.UtcNow
        }, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildPlanReviewAgentId(string runId, string planHash)
    {
        string hashSuffix = string.IsNullOrWhiteSpace(planHash)
            ? Guid.NewGuid().ToString("N")[..12]
            : planHash[..Math.Min(12, planHash.Length)];
        return $"planning-review-{runId}-{hashSuffix}";
    }

    private sealed record PlanApprovalInteractionResult(PlanApproval Approval, IReadOnlyList<PromptAttachment>? Attachments);

    private static bool IsPlanningWorkflow(string? workflow)
        => string.Equals(workflow, WorkflowNames.PLANNING, StringComparison.OrdinalIgnoreCase);

    private static bool IsWikiDocWorkflow(string? workflow)
        => string.Equals(workflow, WorkflowNames.WIKIDOC, StringComparison.OrdinalIgnoreCase);

    private async Task EnsurePlanningSessionForRunStartAsync(
        RunRequest request,
        string workspaceRoot,
        string runId,
        CancellationToken cancellationToken)
    {
        if (this._planningSessionRecorder is null)
        {
            return;
        }

        string? sessionId = request.PlanningSessionId;
        // Only auto-track sessions when explicitly linked or for planning-workflow runs.
        if (string.IsNullOrWhiteSpace(sessionId) && !IsPlanningWorkflow(request.Workflow))
        {
            return;
        }

        try
        {
            string effectiveSessionId = string.IsNullOrWhiteSpace(sessionId) ? runId : sessionId!;
            await this._planningSessionRecorder.EnsureAsync(workspaceRoot, effectiveSessionId, runId, cancellationToken).ConfigureAwait(false);

            // Record the initial user message + attachments so the ledger reflects the full task history.
            await this._planningSessionRecorder.AppendMessageAsync(
                workspaceRoot,
                effectiveSessionId,
                ArchHarness.App.Storage.PlanningSessionRecorder.CreateMessage(
                    ConversationRoles.USER,
                    ConversationMessageKinds.CHAT,
                    request.TaskPrompt ?? string.Empty,
                    request.Attachments,
                    authorAgent: null,
                    relatedRunId: runId),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            // If this is an implementation run continuing a planning session, link it now.
            if (!string.IsNullOrWhiteSpace(request.PlanningSessionId) && !IsPlanningWorkflow(request.Workflow))
            {
                await this._planningSessionRecorder.LinkImplementationRunAsync(
                    workspaceRoot,
                    request.PlanningSessionId!,
                    runId,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            // Best-effort: failing to record planning-session state must not break the run.
        }
    }

    private async Task AppendPlanningSessionMessageAsync(
        RunRequest request,
        string workspaceRoot,
        string runId,
        ConversationMessage message,
        CancellationToken cancellationToken)
    {
        if (this._planningSessionRecorder is null)
        {
            return;
        }

        string? sessionId = request.PlanningSessionId;
        if (string.IsNullOrWhiteSpace(sessionId) && !IsPlanningWorkflow(request.Workflow))
        {
            return;
        }

        try
        {
            string effectiveSessionId = string.IsNullOrWhiteSpace(sessionId) ? runId : sessionId!;
            await this._planningSessionRecorder.AppendMessageAsync(
                workspaceRoot,
                effectiveSessionId,
                message with { RelatedRunId = runId },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort.
        }
    }

    private async Task ExecuteWikiDocWorkflowAsync(
        RunStateCheckpoint checkpoint,
        RunRequest request,
        IProgress<RuntimeProgressEvent>? progress,
        CancellationToken cancellationToken)
    {
        ExecutionPlan plan = new ExecutionPlan(
            new[]
            {
                new ExecutionPlanStep(
                    1,
                    "BackendDeveloper",
                    "Discover Git repositories under the scan root, generate one repository-local wiki Home.md per repository, and record deterministic fallback outputs when a repo-local wiki path cannot be used."),
                new ExecutionPlanStep(
                    2,
                    "BackendDeveloper",
                    "Synthesize a megawiki and shared cross-repository concept pages from the repository documentation outputs.",
                    new[] { 1 },
                    ParallelGroup: 2)
            },
            new IterationStrategy(1, false),
            new[]
            {
                "Every discovered repository has a Home.md output under a repo-local wiki or deterministic fallback path.",
                "A megawiki summary is generated for the scan root.",
                "Cross-repository concept pages are synthesized."
            });

        await this._services.RunInfrastructure.ArtifactWriter.WriteExecutionPlanAsync(checkpoint.RunDirectory, plan, cancellationToken).ConfigureAwait(false);
        await this.WriteRunStateAsync(
            checkpoint,
            new RunProgressSnapshot(
                Array.Empty<int>(),
                0,
                string.Empty,
                Array.Empty<string>(),
                new ArchitectureReview(Array.Empty<ArchitectureFinding>(), Array.Empty<string>()),
                new SecurityReview(Array.Empty<SecurityFinding>(), Array.Empty<string>())),
            RunPhases.EXECUTING_PLAN,
            null,
            cancellationToken).ConfigureAwait(false);

        // Attempt to reconstruct resume state from a prior run's checkpoint or SDK events.
        string scanRoot = Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.WorkspacePath));
        IReadOnlyList<WikiDocRepositoryInfo> repositories = this._wikiDocServices.Discoverer.Discover(scanRoot);
        WikiDocResumeState? wikiDocResume = this._wikiDocServices.ResumeStateBuilder.TryBuild(
            checkpoint.RunDirectory,
            scanRoot,
            repositories,
            this._wikiDocServices.Resolver);

        WikiDocWorkflowResult result = await this._wikiDocServices.Workflow.ExecuteAsync(
            request,
            checkpoint.RunDirectory,
            wikiDocResume,
            progress,
            cancellationToken).ConfigureAwait(false);

        await this.WriteRunStateAsync(
            checkpoint,
            new RunProgressSnapshot(
                plan.Steps.Select(step => step.Id).ToArray(),
                0,
                string.Empty,
                result.FilesTouched,
                new ArchitectureReview(Array.Empty<ArchitectureFinding>(), Array.Empty<string>()),
                new SecurityReview(Array.Empty<SecurityFinding>(), Array.Empty<string>())),
            RunPhases.FINALIZING,
            null,
            cancellationToken).ConfigureAwait(false);
        await this._services.RunInfrastructure.ArtifactWriter.WriteCompletionValidationAsync(checkpoint.RunDirectory, result.ValidationResult, cancellationToken).ConfigureAwait(false);
        await this._services.RunInfrastructure.ArtifactWriter.WriteFinalSummaryAsync(checkpoint.RunDirectory, BuildWikiDocSummary(result.Report, result.ValidationResult), cancellationToken).ConfigureAwait(false);

        string[] modelOverrides = request.ModelOverrides?.Select(pair => $"{pair.Key}={pair.Value}").ToArray() ?? Array.Empty<string>();
        IReadOnlyList<CopilotModelUsage> usage = this._services.SessionContext.CopilotClient.GetUsageSnapshot();
        object[] agentModelUsage = this._services.AgentModelUsageBuilder.Build(request.ModelOverrides);
        bool completed = result.ValidationResult.Passed;

        await this._services.RunInfrastructure.ArtifactWriter.WriteRunLogAsync(checkpoint.RunDirectory, new
        {
            status = completed ? RunStatuses.COMPLETED : RunStatuses.INCOMPLETE,
            projectId = request.ProjectId,
            projectName = request.ProjectName,
            runTitle = request.RunTitle,
            request.WorkspaceMode,
            request.Workflow,
            request.PermissionHandlerMode,
            modelOverrides,
            agents = agentModelUsage,
            copilotUsage = usage,
            wikiDoc = result.Report,
            completionValidation = result.ValidationResult
        }, cancellationToken).ConfigureAwait(false);

        await this._services.RunInfrastructure.EventLogger.AppendEventAsync(checkpoint.RunDirectory, new { runId = checkpoint.RunId, source = WellKnownSources.WIKIDOC, message = RUN_COMPLETED_MESSAGE }, cancellationToken).ConfigureAwait(false);
        await this.WriteRunStateAsync(
            checkpoint,
            new RunProgressSnapshot(
                plan.Steps.Select(step => step.Id).ToArray(),
                0,
                string.Empty,
                result.FilesTouched,
                new ArchitectureReview(Array.Empty<ArchitectureFinding>(), Array.Empty<string>()),
                new SecurityReview(Array.Empty<SecurityFinding>(), Array.Empty<string>()),
                CompletionValidation: result.ValidationResult),
            completed ? RunTerminalPhases.COMPLETED : RunTerminalPhases.INCOMPLETE,
            null,
            cancellationToken,
            completed ? RunStatuses.COMPLETED : RunStatuses.INCOMPLETE).ConfigureAwait(false);
        progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, WellKnownSources.WIKIDOC, RUN_COMPLETED_MESSAGE));
    }

    private static string BuildWikiDocSummary(WikiDocExecutionReport report, CompletionValidationResult validationResult)
    {
        string fallbacks = report.Fallbacks.Count == 0
            ? NONE_LABEL
            : string.Join(", ", report.Fallbacks.Select(fallback => $"{fallback.Scope}:{fallback.ReasonCode}"));
        string conceptPages = report.AggregateOutput.ConceptPagePaths.Count == 0
            ? NONE_LABEL
            : string.Join(", ", report.AggregateOutput.ConceptPagePaths.Select(Path.GetFileName));

        return $"""
            # WikiDoc Summary
            - Completed: {validationResult.Passed}
            - ScanRoot: {report.ScanRoot}
            - RepositoriesDocumented: {report.RepositoryOutputs.Count}
            - MegaWikiPath: {report.AggregateOutput.MegaWikiPath}
            - ConceptPages: {conceptPages}
            - Fallbacks: {fallbacks}
            """;
    }

    private static PlanExecutionResult CreatePlanningPlanExecutionResult(ExecutionPlan plan)
        => new(
            plan,
            new AgentStepExecutor.StepExecutionResult(
                string.Empty,
                Array.Empty<string>(),
                new ArchitectureReview(Array.Empty<ArchitectureFinding>(), Array.Empty<string>()),
                new SecurityReview(Array.Empty<SecurityFinding>(), Array.Empty<string>())));

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
        SecurityReview SecurityReview,
        ClarificationSpec? Spec = null,
        PlanApproval? Approval = null,
        BuildOutcome? LastBuildOutcome = null,
        CompletionValidationResult? CompletionValidation = null,
        IReadOnlyList<ClarificationAnswer>? ClarificationAnswers = null);
}
