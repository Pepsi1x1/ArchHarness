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
    private const string RUN_STARTED_MESSAGE = "Run started";
    private const string RUN_RESUMED_MESSAGE = "Run resumed";

    private readonly OrchestratorRunServices _services;
    private readonly RuntimeStateAccessors _stateAccessors;
    private readonly IRunAgentModelUsageBuilder _agentModelUsageBuilder;
    private readonly IPlanApprovalBridge? _approvalBridge;
    private readonly ICopilotUserInputBridge? _userInputBridge;
    private readonly OrchestrationAgent _orchestrationAgent;
    private readonly PlanningAgent _planningAgent;
    private readonly IRunVerificationWorkflow _verificationWorkflow;
    private readonly WikiDocRunServices _wikiDocServices;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrchestratedRunProcessor"/> class.
    /// </summary>
    public OrchestratedRunProcessor(
        OrchestratorRunServices services,
        RuntimeStateAccessors stateAccessors,
        IRunAgentModelUsageBuilder agentModelUsageBuilder,
        OrchestrationAgent orchestrationAgent,
        PlanningAgent planningAgent,
        IRunVerificationWorkflow verificationWorkflow,
        WikiDocRunServices wikiDocServices,
        IPlanApprovalBridge? approvalBridge = null,
        ICopilotUserInputBridge? userInputBridge = null)
    {
        this._services = services;
        this._stateAccessors = stateAccessors;
        this._agentModelUsageBuilder = agentModelUsageBuilder;
        this._orchestrationAgent = orchestrationAgent;
        this._planningAgent = planningAgent;
        this._verificationWorkflow = verificationWorkflow;
        this._wikiDocServices = wikiDocServices;
        this._approvalBridge = approvalBridge;
        this._userInputBridge = userInputBridge;
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

            (ExecutionPlan plan, PlanExecutionResult planResult, ClarificationSpec? spec, IReadOnlyList<ClarificationAnswer> clarificationAnswers) = await this.ExecutePlanAsync(context, progress, runId, runDirectory, cancellationToken).ConfigureAwait(false);

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
                adapter,
                frontendPlan,
                filesTouched,
                review,
                securityReview,
                resumeState?.ReviewIteration ?? 0,
                spec,
                clarificationAnswers,
                lastBuildOutcome,
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
                            request,
                            adapter,
                            plan,
                            resumeSpec,
                            resumeState.ClarificationAnswers ?? Array.Empty<ClarificationAnswer>(),
                            runId,
                            runDirectory,
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
                OrchestrationAgent planningAgent = this.ResolvePlanningAgent(request.Workflow);
                progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, WellKnownSources.ORCHESTRATOR, "Generating clarification spec"));
                (spec, clarificationAnswers) = await this.RunClarificationLoopAsync(
                    request,
                    adapter.RootPath,
                    runId,
                    runDirectory,
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
                new PlanningContext(spec, clarificationAnswers),
                cancellationToken).ConfigureAwait(false);
            progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, WellKnownSources.ORCHESTRATOR, "Execution plan built"));

            // Phase: Plan approval (when bridge is available)
            if (IsPlanningWorkflow(request.Workflow))
            {
                builtPlan = await this.RunPlanningApprovalLoopAsync(
                    request,
                    adapter,
                    builtPlan,
                    spec,
                    clarificationAnswers,
                    runId,
                    runDirectory,
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

    private async Task<PlanApproval?> RequestPlanApprovalAsync(
        ClarificationSpec? spec,
        ExecutionPlan plan,
        string runId,
        string runDirectory,
        CancellationToken cancellationToken)
    {
        if (this._approvalBridge is null)
        {
            return new PlanApproval(PlanApprovalDecisions.APPROVED, DateTimeOffset.UtcNow, string.Empty);
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

        string planSummary = string.Join(Environment.NewLine, plan.Steps.Select(s => $"  {s.Id}. [{s.Agent}] {s.Objective}"));
        string specMarkdown = spec is not null
            ? $"Task: {spec.Task}\nOutcome: {spec.DesiredOutcome}\nCriteria: {string.Join(", ", spec.AcceptanceCriteria)}"
            : "(no spec generated)";

        PlanApprovalResponse response = await this._approvalBridge.RequestApprovalAsync(
            new PlanApprovalRequest(
                spec ?? new ClarificationSpec(string.Empty, string.Empty, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()),
                plan,
                specMarkdown,
                planSummary),
            cancellationToken).ConfigureAwait(false);

        string planHash = plan.Steps.Count.ToString();
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

        return approval;
    }

    private async Task<(ClarificationSpec Spec, IReadOnlyList<ClarificationAnswer> Answers)> RunClarificationLoopAsync(
        RunRequest request,
        string workspaceRoot,
        string runId,
        string runDirectory,
        IReadOnlyList<ClarificationAnswer> existingAnswers,
        OrchestrationAgent clarificationAgent,
        IProgress<RuntimeProgressEvent>? progress,
        CancellationToken cancellationToken)
    {
        List<ClarificationAnswer> clarificationAnswers = existingAnswers.ToList();
        ClarificationSpec spec = await clarificationAgent.BuildClarificationSpecAsync(
            request,
            workspaceRoot,
            clarificationAnswers,
            clarificationAgent.Id,
            clarificationAgent.Role,
            cancellationToken).ConfigureAwait(false);

        await this.PersistClarificationStateAsync(runId, runDirectory, spec, clarificationAnswers, cancellationToken).ConfigureAwait(false);

        if (!IsPlanningWorkflow(request.Workflow) || this._userInputBridge is null)
        {
            return (spec, clarificationAnswers);
        }

        for (int round = 1; round <= MAX_CLARIFICATION_ROUNDS && spec.OpenQuestions.Count > 0; round++)
        {
            List<string> unansweredQuestions = spec.OpenQuestions
                .Where(question => clarificationAnswers.All(answer => !string.Equals(answer.Question, question, StringComparison.Ordinal)))
                .ToList();

            if (unansweredQuestions.Count == 0)
            {
                break;
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

            IReadOnlyList<UserInputResponse> responses = unansweredQuestions.Count == 1
                ? new[] { await this._userInputBridge.RequestInputAsync(requests[0]).ConfigureAwait(false) }
                : await this._userInputBridge.RequestInputsAsync(requests).ConfigureAwait(false);

            for (int index = 0; index < unansweredQuestions.Count; index++)
            {
                UserInputResponse response = index < responses.Count
                    ? responses[index]
                    : new UserInputResponse { Answer = string.Empty, WasFreeform = true };
                clarificationAnswers.Add(new ClarificationAnswer(unansweredQuestions[index], response.Answer ?? string.Empty));
            }

            await this.PersistClarificationStateAsync(runId, runDirectory, spec, clarificationAnswers, cancellationToken).ConfigureAwait(false);

            progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, WellKnownSources.ORCHESTRATOR, "Regenerating clarification spec"));
            spec = await clarificationAgent.BuildClarificationSpecAsync(
                request,
                workspaceRoot,
                clarificationAnswers,
                clarificationAgent.Id,
                clarificationAgent.Role,
                cancellationToken).ConfigureAwait(false);
            await this.PersistClarificationStateAsync(runId, runDirectory, spec, clarificationAnswers, cancellationToken).ConfigureAwait(false);
        }

        return (spec, clarificationAnswers);
    }

    private async Task<ExecutionPlan> RunPlanningApprovalLoopAsync(
        RunRequest request,
        IWorkspaceAdapter adapter,
        ExecutionPlan plan,
        ClarificationSpec? spec,
        IReadOnlyList<ClarificationAnswer> clarificationAnswers,
        string runId,
        string runDirectory,
        IProgress<RuntimeProgressEvent>? progress,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            PlanApproval? approval = await this.RequestPlanApprovalAsync(spec, plan, runId, runDirectory, cancellationToken).ConfigureAwait(false);
            if (approval is null || string.Equals(approval.Decision, PlanApprovalDecisions.CANCELED, StringComparison.OrdinalIgnoreCase))
            {
                throw new OperationCanceledException("Plan approval was canceled by user.");
            }

            if (!string.Equals(approval.Decision, PlanApprovalDecisions.REGENERATE, StringComparison.OrdinalIgnoreCase))
            {
                return plan;
            }

            progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, WellKnownSources.ORCHESTRATOR, "Regenerating execution plan", approval.Reason));
            plan = await this._services.RunPhases.PlanExecutor.BuildPlanAsync(
                request,
                adapter,
                runId,
                runDirectory,
                new PlanningContext(spec, clarificationAnswers, approval.Reason),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private OrchestrationAgent ResolvePlanningAgent(string workflow)
        => string.Equals(workflow, WorkflowNames.PLANNING, StringComparison.OrdinalIgnoreCase)
            ? this._planningAgent
            : this._orchestrationAgent;

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
        IReadOnlyList<object> agentModelUsage = this._agentModelUsageBuilder.Build(request.ModelOverrides);

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
        IWorkspaceAdapter adapter,
        string frontendPlan,
        IReadOnlyList<string> filesTouched,
        ArchitectureReview review,
        SecurityReview securityReview,
        int reviewIteration,
        ClarificationSpec? spec,
        IReadOnlyList<ClarificationAnswer> clarificationAnswers,
        BuildOutcome? lastBuildOutcome,
        IProgress<RuntimeProgressEvent>? progress,
        CancellationToken cancellationToken)
    {
        await this._services.RunInfrastructure.ArtifactWriter.WriteArchitectureReviewAsync(checkpoint.RunDirectory, review, cancellationToken).ConfigureAwait(false);
        await this._services.RunInfrastructure.ArtifactWriter.WriteSecurityReviewAsync(checkpoint.RunDirectory, securityReview, cancellationToken).ConfigureAwait(false);
        await this.WriteRunStateAsync(
            checkpoint,
            new RunProgressSnapshot(plan.Steps.Select(step => step.Id).ToArray(), reviewIteration, frontendPlan, filesTouched, review, securityReview, Spec: spec, LastBuildOutcome: lastBuildOutcome, ClarificationAnswers: clarificationAnswers),
            RunPhases.FINALIZING,
            null,
            cancellationToken).ConfigureAwait(false);

        VerificationWorkflowResult verificationResult = await this._verificationWorkflow.RunAsync(
            new RunVerificationRequest(request, plan, review, securityReview, spec, lastBuildOutcome, filesTouched),
            adapter,
            progress,
            cancellationToken).ConfigureAwait(false);
        lastBuildOutcome = verificationResult.LastBuildOutcome;
        filesTouched = verificationResult.FilesTouched;
        CompletionValidationResult validationResult = verificationResult.ValidationResult;
        bool completed = validationResult.Passed;
        await this._services.RunInfrastructure.ArtifactWriter.WriteCompletionValidationAsync(checkpoint.RunDirectory, validationResult, cancellationToken).ConfigureAwait(false);
        await this._services.RunInfrastructure.ArtifactWriter.WriteFinalSummaryAsync(
            checkpoint.RunDirectory,
            BuildFinalSummary(frontendPlan, filesTouched, review, securityReview, validationResult),
            cancellationToken).ConfigureAwait(false);

        string[] modelOverrides = request.ModelOverrides?.Select(pair => $"{pair.Key}={pair.Value}").ToArray() ?? Array.Empty<string>();
        IReadOnlyList<CopilotModelUsage> usage = this._services.SessionContext.CopilotClient.GetUsageSnapshot();
        object[] agentModelUsage = this._agentModelUsageBuilder.Build(request.ModelOverrides);

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
            new RunProgressSnapshot(plan.Steps.Select(step => step.Id).ToArray(), reviewIteration, frontendPlan, filesTouched, review, securityReview, Spec: spec, LastBuildOutcome: lastBuildOutcome, CompletionValidation: validationResult, ClarificationAnswers: clarificationAnswers),
            completed ? RunTerminalPhases.COMPLETED : RunTerminalPhases.INCOMPLETE,
            null,
            cancellationToken,
            completed ? RunStatuses.COMPLETED : RunStatuses.INCOMPLETE).ConfigureAwait(false);
        progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, WellKnownSources.ORCHESTRATOR, RUN_COMPLETED_MESSAGE));
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
                HandoffRunId: existingState?.HandoffRunId),
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
            ? "(none)"
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
            ? "(none)"
            : string.Join(", ", spec.AcceptanceCriteria);
        string stepList = plan.Steps.Count == 0
            ? "(none)"
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

    private static bool IsPlanningWorkflow(string? workflow)
        => string.Equals(workflow, WorkflowNames.PLANNING, StringComparison.OrdinalIgnoreCase);

    private static bool IsWikiDocWorkflow(string? workflow)
        => string.Equals(workflow, WorkflowNames.WIKIDOC, StringComparison.OrdinalIgnoreCase);

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

        WikiDocWorkflowResult result = await this._wikiDocServices.Workflow.ExecuteAsync(
            request,
            checkpoint.RunDirectory,
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
        object[] agentModelUsage = this._agentModelUsageBuilder.Build(request.ModelOverrides);
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
            ? "(none)"
            : string.Join(", ", report.Fallbacks.Select(fallback => $"{fallback.Scope}:{fallback.ReasonCode}"));
        string conceptPages = report.AggregateOutput.ConceptPagePaths.Count == 0
            ? "(none)"
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
