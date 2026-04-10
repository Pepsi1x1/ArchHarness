using ArchHarness.App.Constants;
using ArchHarness.App.Core;
using ArchHarness.App.Copilot;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Agents;

/// <summary>
/// Orchestration agent responsible for planning execution steps, building remediation prompts,
/// and validating run completion.
/// </summary>
public class OrchestrationAgent : AgentBase
{
    private const string ORCHESTRATION_PROMPT_GROUP_NAME = "Orchestration";
    private const string DEFAULT_ARCH_LOOP_TASK_PROMPT_FALLBACK = DefaultPrompts.ARCHITECTURE_LOOP_TASK;
    private const string NONE_LABEL = "(none)";

    private const string CLARIFICATION_SPEC_PROMPT_FALLBACK = """
        You are the orchestration planner. Analyze the task prompt and produce a clarification spec as strict JSON with this schema:
        {
            "task": "string",
            "desiredOutcome": "string",
            "inScope": ["string"],
            "outOfScope": ["string"],
            "constraints": ["string"],
            "assumptions": ["string"],
            "acceptanceCriteria": ["string"],
            "likelyTouchpoints": ["string"],
            "openQuestions": ["string"],
            "decisionNotes": ["string"],
            "verificationCommands": [{"name":"string","command":"string","evidenceType":"build|test|lint|typecheck|runtime|manual","criterion":"string","required":true}]
        }

        TaskPrompt: {{TaskPrompt}}
        WorkspaceRoot: {{WorkspaceRoot}}
        WorkspaceMode: {{WorkspaceMode}}
        BuildCommand: {{BuildCommand}}
        {{ClarificationAnswersSection}}
        """;

    private const string ORCHESTRATION_SYSTEM_INSTRUCTIONS_FALLBACK = """
        You are the orchestration planner.
        Your role is planning and delegation only.
        Never modify workspace files directly and never perform implementation work.
        Never invoke file editing tools, including edit_file, this is the delegated agents job.
        Produce delegated prompts and validation outputs for specialized agents.
        """;
    private const string ORCHESTRATION_PLANNING_PROMPT_FALLBACK = """
        You are the orchestration planner. Return ONLY strict JSON with this schema:
        {
            "steps": [{"id":1,"agent":"FrontendDeveloper|BackendDeveloper|Build|CodingStyle|Security|Architecture","objective":"string","dependsOn":[1],"languages":["dotnet","vue3"]}],
            "iterationStrategy": {"maxIterations": 2, "reviewRequired": true},
            "completionCriteria": ["string"]
        }

        Constraints:
        - Enabled review/enforcement agents for this run: {{EnabledReviewLoopAgents}}.
        - Disabled review/enforcement agents for this run: {{DisabledReviewLoopAgents}}.
        - The harness auto-injects only enabled review steps after implementation or build work when they are omitted.
        - Include FrontendDeveloper when UI/UX work is implied.
        - Include BackendDeveloper when backend or middle-tier implementation is implied.
        - Use Build for baseline, intermediate, or final validation build execution and build-result triage.
        - Do not ask FrontendDeveloper or BackendDeveloper to run baseline or validation builds.
        - CodingStyle, Security, and Architecture are review/enforcement steps when explicitly included and enabled.
        - Never include a disabled review/enforcement agent in steps.
        - When CodingStyle and Security are both enabled, CodingStyle must execute before Security.
        - When Security and Architecture are both enabled, Security must execute before Architecture.
        - When Architecture is enabled, it must be a single final review/enforcement step only.
        - When a final validation build is needed, represent it as a Build step that depends on the last enabled review/enforcement step and runs after all enabled review/enforcement steps.
        - Never use Architecture for solution design/spec generation/planning.
        - Never use CodingStyle for solution design/spec generation/planning.
        - Never use Security for solution design/spec generation/planning.
        - Never use Build for source-code implementation work.
        - Use dependsOn to encode step dependencies when a step requires outputs from prior steps.
        - If a step has no dependencies, omit dependsOn or set it to []. Do NOT use 0.
        - Use languages on CodingStyle/Security/Architecture steps to declare review scope (dotnet and/or vue3).
        - All filesystem paths in objectives must be under WorkspaceRoot.
        - Do not use directories relative to process CWD; always anchor to WorkspaceRoot.
        - Use as many steps as necessary; do not pad or compress the plan to hit a target step count.
        - completionCriteria should match the enabled review agents for this run plus build verification:
        {{ReviewLoopCompletionCriteria}}
        - Each objective must be a concrete delegated prompt the target agent can execute directly.
        - If ArchitectureLoopMode is true, enabled Security and Architecture objective(s) must review and enforce over the entire WorkspaceRoot.
        - Use the approved clarification context when it is present. Treat clarification answers as resolved requirements, not as open design questions.
        - When PlanRevisionRequest is present, treat it as mandatory feedback for how the plan must change. It may request specific refinements or a materially different plan shape.

        TaskPrompt: {{TaskPrompt}}
        WorkspaceRoot: {{WorkspaceRoot}}
        WorkspaceMode: {{WorkspaceMode}}
        BuildCommand: {{BuildCommand}}
        ArchitectureLoopMode: {{ArchitectureLoopMode}}
        ArchitectureLoopPrompt: {{ArchitectureLoopPrompt}}
        {{ClarificationSpecSection}}
        {{ClarificationAnswersSection}}
        {{PlanRevisionRequestSection}}
        """;
    private const string ORCHESTRATION_REMEDIATION_PROMPT_FALLBACK = """
        You are the orchestration planner.
        Generate a single delegated prompt for the enabled review-loop agents.
        Focus only on remediation actions from the active review findings.
        Return plain text only (no markdown, no JSON).

        Iteration: {{Iteration}}
        OriginalTask: {{OriginalTask}}
        WorkspaceRoot: {{WorkspaceRoot}}
        ArchitectureLoopMode: {{ArchitectureLoopMode}}
        {{RequiredActionsSection}}
        {{ArchitectureLoopPromptSection}}
        """;
    private const string ORCHESTRATION_VERIFIER_PROMPT_FALLBACK = """
        You are Verifier. Prove or disprove completion with concrete evidence.

        Return ONLY strict JSON with this schema:
        {
            "verdict": "PASS|FAIL|INCOMPLETE",
            "materiallyImplemented": true,
            "summary": "string",
            "evidence": ["string"],
            "gaps": ["string"],
            "risks": ["string"]
        }

        Rules:
        - Verify claims against code, relevant files, diffs, commands, tests, and recorded evidence.
        - Do not trust unverified implementation claims.
        - Distinguish missing evidence from failed behavior.
        - Passing commands alone is insufficient if the core requested behavior or plan outcomes are not materially present in the workspace.
        - materiallyImplemented must be false when the core requested work is absent, partial, or only weakly evidenced.
        - Prefer direct evidence over reassurance.

        Task: {{Task}}
        DesiredOutcome: {{DesiredOutcome}}
        AcceptanceCriteria:
        {{AcceptanceCriteriaSection}}

        PlanSteps:
        {{PlanStepsSection}}

        FilesTouched:
        {{FilesTouchedSection}}

        BuildOutcome:
        {{BuildOutcomeSection}}

        VerificationEvidence:
        {{VerificationEvidenceSection}}

        DeterministicChecks:
        {{DeterministicChecksSection}}

        ReviewSummary:
        {{ReviewSummarySection}}
        """;

    private readonly IExecutionPlanParser _executionPlanParser;
    private readonly AgentsOptions _agentsOptions;
    private readonly IReviewLoopAgentSelectionAccessor _reviewLoopAgentSelectionAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrchestrationAgent"/> class.
    /// </summary>
    public OrchestrationAgent(
        ICopilotClient copilotClient,
        IModelResolver modelResolver,
        IAgentToolPolicyProvider toolPolicyProvider,
        IOptions<AgentsOptions> agentsOptions,
        IReviewLoopAgentSelectionAccessor reviewLoopAgentSelectionAccessor,
        IExecutionPlanParser executionPlanParser)
        : this(copilotClient, modelResolver, toolPolicyProvider, agentsOptions, reviewLoopAgentSelectionAccessor, executionPlanParser, "orchestration")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="OrchestrationAgent"/> class for a specific orchestration-style role.
    /// </summary>
    protected OrchestrationAgent(
        ICopilotClient copilotClient,
        IModelResolver modelResolver,
        IAgentToolPolicyProvider toolPolicyProvider,
        IOptions<AgentsOptions> agentsOptions,
        IReviewLoopAgentSelectionAccessor reviewLoopAgentSelectionAccessor,
        IExecutionPlanParser executionPlanParser,
        string role)
        : base(copilotClient, modelResolver, toolPolicyProvider, agentsOptions, role, Guid.NewGuid().ToString("N"))
    {
        this._agentsOptions = agentsOptions.Value;
        this._reviewLoopAgentSelectionAccessor = reviewLoopAgentSelectionAccessor;
        this._executionPlanParser = executionPlanParser;
    }

    /// <summary>
    /// Returns the completion options used for warm-up calls with the orchestration system instructions and tool policy applied.
    /// </summary>
    internal CopilotCompletionOptions GetWarmUpCompletionOptions()
        => base.ApplyToolPolicy(CreateOrchestrationCompletionOptions());

    /// <summary>
    /// Builds an execution plan by prompting the orchestration model and parsing the returned JSON into an <see cref="ExecutionPlan"/>.
    /// </summary>
    /// <param name="request">The run request containing the task prompt and configuration.</param>
    /// <param name="workspaceRoot">The root path of the target workspace.</param>
    /// <param name="agentId">Optional agent identifier override.</param>
    /// <param name="agentRole">Optional agent role override.</param>
    /// <param name="cancellationToken">Token to signal cancellation.</param>
    /// <returns>The parsed execution plan.</returns>
    public async Task<ExecutionPlan> BuildExecutionPlanAsync(
        RunRequest request,
        string workspaceRoot,
        PlanningContext? planningContext = null,
        string? agentId = null,
        string? agentRole = null,
        CancellationToken cancellationToken = default)
    {
        string model = base.ResolveModel(request.ModelOverrides);
        string buildCommand = request.BuildCommand ?? NONE_LABEL;
        bool architectureLoopMode = request.ArchitectureLoopMode;
        string architectureLoopPrompt = request.ArchitectureLoopPrompt ?? NONE_LABEL;
        string effectiveTaskPrompt = ResolveTaskPrompt(request.TaskPrompt, architectureLoopMode);
        ReviewLoopAgentSelection reviewLoopAgents = request.ReviewLoopAgents
            ?? this._reviewLoopAgentSelectionAccessor.Current
            ?? this._agentsOptions.GetReviewLoopAgentSelection();

        string planningTemplate = PromptLoader.Load(ORCHESTRATION_PROMPT_GROUP_NAME, "planning.md", ORCHESTRATION_PLANNING_PROMPT_FALLBACK);
        string planningPrompt = PromptLoader.Render(
            planningTemplate,
            ("{{TaskPrompt}}", effectiveTaskPrompt),
            ("{{WorkspaceRoot}}", workspaceRoot),
            ("{{WorkspaceMode}}", request.WorkspaceMode),
            ("{{BuildCommand}}", buildCommand),
            ("{{ArchitectureLoopMode}}", architectureLoopMode.ToString()),
            ("{{ArchitectureLoopPrompt}}", architectureLoopPrompt),
            ("{{ClarificationSpecSection}}", BuildClarificationSpecSection(planningContext?.Spec)),
            ("{{ClarificationAnswersSection}}", BuildClarificationAnswersSection(planningContext?.ClarificationAnswers)),
            ("{{PlanRevisionRequestSection}}", BuildPlanRevisionRequestSection(planningContext?.PlanRevisionRequest)),
            ("{{EnabledReviewLoopAgents}}", reviewLoopAgents.DescribeEnabledAgents()),
            ("{{DisabledReviewLoopAgents}}", reviewLoopAgents.DescribeDisabledAgents()),
            ("{{ReviewLoopCompletionCriteria}}", string.Join(Environment.NewLine, reviewLoopAgents.BuildCompletionCriteria().Select(x => $"- {x}"))));

        CopilotCompletionOptions options = base.ApplyToolPolicy(CreateOrchestrationCompletionOptions());
        const int MAX_PLANNING_ATTEMPTS = 3;
        string? lastResponse = null;
        string? lastValidationError = null;

        for (int attempt = 1; attempt <= MAX_PLANNING_ATTEMPTS; attempt++)
        {
            string? priorResponsePreview = lastResponse?.Length > 1200 ? lastResponse[..1200] + "..." : lastResponse;
            string promptForAttempt = attempt == 1
                ? planningPrompt
                : $"{planningPrompt}\n\nIMPORTANT: Your previous response could not be parsed. Return ONLY the raw JSON object. No markdown, no code fences, no commentary.\nValidation error: {lastValidationError ?? "Unknown validation error."}\nPrevious response:\n{priorResponsePreview}";

            lastResponse = await base.CopilotClient.CompleteAsync(
                model,
                promptForAttempt,
                options,
                agentId: agentId ?? base.Id,
                agentRole: agentRole ?? base.Role,
                cancellationToken);

            if (this._executionPlanParser.TryBuildExecutionPlan(lastResponse, workspaceRoot, out ExecutionPlan parsedPlan, out lastValidationError))
            {
                return request.ArchitectureLoopMode
                    ? ApplyArchitectureLoopMode(parsedPlan, request, reviewLoopAgents)
                    : parsedPlan;
            }
        }

        string? preview = lastResponse?.Length > 500 ? lastResponse[..500] + "..." : lastResponse;
        throw new InvalidOperationException(
            $"Orchestration model did not return a valid ExecutionPlan JSON after {MAX_PLANNING_ATTEMPTS} attempts.\n" +
            $"Validation error: {lastValidationError}\n" +
            $"Last response preview: {preview}");
    }

    /// <summary>
    /// Generates a clarification/spec artifact from the task prompt and workspace context.
    /// The spec becomes the authoritative contract for plan generation and completion validation.
    /// </summary>
    public async Task<ClarificationSpec> BuildClarificationSpecAsync(
        RunRequest request,
        string workspaceRoot,
        IReadOnlyList<ClarificationAnswer>? clarificationAnswers = null,
        string? agentId = null,
        string? agentRole = null,
        CancellationToken cancellationToken = default)
    {
        string model = base.ResolveModel(request.ModelOverrides);
        string buildCommand = request.BuildCommand ?? NONE_LABEL;

        string specTemplate = PromptLoader.Load(ORCHESTRATION_PROMPT_GROUP_NAME, "clarification-spec.md", CLARIFICATION_SPEC_PROMPT_FALLBACK);
        string specPrompt = PromptLoader.Render(
            specTemplate,
            ("{{TaskPrompt}}", request.TaskPrompt),
            ("{{WorkspaceRoot}}", workspaceRoot),
            ("{{WorkspaceMode}}", request.WorkspaceMode),
            ("{{BuildCommand}}", buildCommand),
            ("{{ClarificationAnswersSection}}", BuildClarificationAnswersSection(clarificationAnswers)));

        CopilotCompletionOptions options = base.ApplyToolPolicy(CreateOrchestrationCompletionOptions());
        const int MAX_SPEC_ATTEMPTS = 3;
        string? lastResponse = null;

        for (int attempt = 1; attempt <= MAX_SPEC_ATTEMPTS; attempt++)
        {
            string previousResponsePreview = lastResponse?.Length > 1200 ? lastResponse[..1200] + "..." : lastResponse ?? string.Empty;
            string promptForAttempt = attempt == 1
                ? specPrompt
                : $"{specPrompt}\n\nIMPORTANT: Your previous response could not be parsed. Return ONLY the raw JSON object. No markdown, no code fences, no commentary.\nPrevious response:\n{previousResponsePreview}";

            lastResponse = await base.CopilotClient.CompleteAsync(
                model,
                promptForAttempt,
                options,
                agentId: agentId ?? base.Id,
                agentRole: agentRole ?? base.Role,
                cancellationToken);

            if (TryParseClarificationSpec(lastResponse, out ClarificationSpec? spec))
            {
                return spec;
            }
        }

        string? responsePreview = lastResponse?.Length > 500 ? lastResponse[..500] + "..." : lastResponse;
        throw new InvalidOperationException(
            $"Orchestration model did not return a valid ClarificationSpec JSON after {MAX_SPEC_ATTEMPTS} attempts.\n" +
            $"Last response preview: {responsePreview}");
    }

    private static string BuildClarificationSpecSection(ClarificationSpec? spec)
    {
        if (spec is null)
        {
            return string.Empty;
        }

        string verificationCommands = spec.VerificationCommands is { Count: > 0 }
            ? string.Join("; ", spec.VerificationCommands.Select(command => $"{command.Name}: {command.Command}"))
            : NONE_LABEL;

        return $"""
            ClarificationSpec:
            - Task: {spec.Task}
            - DesiredOutcome: {spec.DesiredOutcome}
            - InScope: {JoinValues(spec.InScope)}
            - OutOfScope: {JoinValues(spec.OutOfScope)}
            - Constraints: {JoinValues(spec.Constraints)}
            - Assumptions: {JoinValues(spec.Assumptions)}
            - AcceptanceCriteria: {JoinValues(spec.AcceptanceCriteria)}
            - LikelyTouchpoints: {JoinValues(spec.LikelyTouchpoints)}
            - DecisionNotes: {JoinValues(spec.DecisionNotes)}
            - VerificationCommands: {verificationCommands}
            """;
    }

    private static string BuildClarificationAnswersSection(IReadOnlyList<ClarificationAnswer>? clarificationAnswers)
    {
        if (clarificationAnswers is not { Count: > 0 })
        {
            return string.Empty;
        }

        return $"ClarificationAnswers:{Environment.NewLine}{string.Join(Environment.NewLine, clarificationAnswers.Select(answer => $"- Q: {answer.Question}{Environment.NewLine}  A: {answer.Answer}"))}";
    }

    private static string BuildPlanRevisionRequestSection(string? planRevisionRequest)
    {
        if (string.IsNullOrWhiteSpace(planRevisionRequest))
        {
            return string.Empty;
        }

        return $"PlanRevisionRequest:{Environment.NewLine}{planRevisionRequest.Trim()}";
    }

    private static string JoinValues(IReadOnlyList<string> values)
        => values.Count == 0 ? NONE_LABEL : string.Join("; ", values);

    /// <summary>
    /// Generates a delegated remediation prompt for the Architecture agent based on outstanding required actions.
    /// </summary>
    /// <param name="request">The run request containing task context and configuration.</param>
    /// <param name="workspaceRoot">The root path of the target workspace.</param>
    /// <param name="requiredActions">The list of required remediation actions from the review.</param>
    /// <param name="iteration">The current remediation iteration number.</param>
    /// <param name="agentId">Optional agent identifier override.</param>
    /// <param name="agentRole">Optional agent role override.</param>
    /// <param name="cancellationToken">Token to signal cancellation.</param>
    /// <returns>A plain-text delegated prompt for the Architecture agent.</returns>
    public async Task<string> BuildRemediationPromptAsync(
        RunRequest request,
        string workspaceRoot,
        IReadOnlyList<string> requiredActions,
        int iteration,
        string? agentId = null,
        string? agentRole = null,
        CancellationToken cancellationToken = default)
    {
        string model = base.ResolveModel(request.ModelOverrides);
        string effectiveTaskPrompt = ResolveTaskPrompt(request.TaskPrompt, request.ArchitectureLoopMode);
        string reviewSummary = string.Join(Environment.NewLine, requiredActions.Select(x => $"- {x}"));
        string requiredActionsSection = string.IsNullOrWhiteSpace(reviewSummary)
            ? string.Empty
            : $"{Environment.NewLine}RequiredActions:{Environment.NewLine}{reviewSummary}";

        string architectureLoopPromptSection = string.IsNullOrWhiteSpace(request.ArchitectureLoopPrompt)
            ? string.Empty
            : $"{Environment.NewLine}ArchitectureLoopPrompt:{Environment.NewLine}{request.ArchitectureLoopPrompt}";

        string remediationTemplate = PromptLoader.Load(ORCHESTRATION_PROMPT_GROUP_NAME, "remediation.md", ORCHESTRATION_REMEDIATION_PROMPT_FALLBACK);
        string prompt = PromptLoader.Render(
            remediationTemplate,
            ("{{Iteration}}", iteration.ToString()),
            ("{{OriginalTask}}", effectiveTaskPrompt),
            ("{{WorkspaceRoot}}", workspaceRoot),
            ("{{ArchitectureLoopMode}}", request.ArchitectureLoopMode.ToString()),
            ("{{RequiredActionsSection}}", requiredActionsSection),
            ("{{ArchitectureLoopPromptSection}}", architectureLoopPromptSection));

        CopilotCompletionOptions options = base.ApplyToolPolicy(CreateOrchestrationCompletionOptions());
        string response = await base.CopilotClient.CompleteAsync(
            model,
            prompt,
            options,
            agentId: agentId ?? base.Id,
            agentRole: agentRole ?? base.Role,
            cancellationToken);

        string remediationPrompt = string.IsNullOrWhiteSpace(response)
            ? $"Enforce all architecture required actions for iteration {iteration} directly in workspace files and re-check SOLID/DRY compliance."
            : response.Trim();

        return request.ArchitectureLoopMode
            ? BuildArchitectureLoopObjective(remediationPrompt, request.ArchitectureLoopPrompt)
            : remediationPrompt;
    }

    /// <summary>
    /// Validates whether the run has met its completion criteria based on review findings, build status,
    /// and approved spec acceptance criteria.
    /// </summary>
    /// <param name="request">The completion validation request containing the plan, reviews, spec, and build results.</param>
    /// <param name="agentId">Optional agent identifier override.</param>
    /// <param name="agentRole">Optional agent role override.</param>
    /// <param name="cancellationToken">Token to signal cancellation.</param>
    /// <returns><see langword="true"/> if all completion criteria are met; otherwise <see langword="false"/>.</returns>
    public async Task<CompletionValidationResult> ValidateCompletionAsync(
        CompletionValidationRequest request,
        string? agentId = null,
        string? agentRole = null,
        CancellationToken cancellationToken = default)
    {
        List<CriterionResult> results = new List<CriterionResult>();
        ReviewLoopAgentSelection reviewLoopAgents = this._reviewLoopAgentSelectionAccessor.Current
            ?? this._agentsOptions.GetReviewLoopAgentSelection();

        // Evaluate each plan completion criterion.
        foreach (string criterion in request.Plan.CompletionCriteria)
        {
            CriterionResult result = CompletionCriteriaSupport.EvaluateCriterion(criterion, request, reviewLoopAgents);
            results.Add(result);
        }

        // If the spec has acceptance criteria, evaluate those too.
        if (request.Spec is not null)
        {
            foreach (string acceptanceCriterion in request.Spec.AcceptanceCriteria)
            {
                // Skip if it's already effectively covered by a plan criterion.
                if (results.Any(r => string.Equals(r.Criterion, acceptanceCriterion, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                CriterionResult result = CompletionCriteriaSupport.EvaluateCriterion(acceptanceCriterion, request, reviewLoopAgents);
                results.Add(result);
            }
        }

        ImplementationAssessment assessment = await this.BuildImplementationAssessmentAsync(
            request,
            results,
            agentId,
            agentRole,
            cancellationToken).ConfigureAwait(false);
        results.Add(new CriterionResult(
            "Plan materially implemented",
            assessment.MateriallyImplemented,
            BuildImplementationEvidenceSummary(assessment)));

        // Store the detailed results for later retrieval.
        this._lastValidationResult = new CompletionValidationResult(
            results.All(r => r.Passed),
            results,
            Summary: BuildValidationSummary(results, assessment),
            Confidence: ResolveValidationConfidence(request, assessment),
            Evidence: request.VerificationEvidence,
            Assessment: assessment);

        return this._lastValidationResult;
    }

    /// <summary>
    /// Returns the detailed results from the most recent completion validation, if available.
    /// </summary>
    public CompletionValidationResult? LastValidationResult => this._lastValidationResult;
    private CompletionValidationResult? _lastValidationResult;

    private async Task<ImplementationAssessment> BuildImplementationAssessmentAsync(
        CompletionValidationRequest request,
        IReadOnlyList<CriterionResult> deterministicResults,
        string? agentId,
        string? agentRole,
        CancellationToken cancellationToken)
    {
        string model = base.ResolveModel(request.ModelOverrides);
        CopilotCompletionOptions options = base.ApplyToolPolicy(CreateOrchestrationCompletionOptions());
        string template = PromptLoader.Load(ORCHESTRATION_PROMPT_GROUP_NAME, "verifier.md", ORCHESTRATION_VERIFIER_PROMPT_FALLBACK);
        string prompt = PromptLoader.Render(
            template,
            ("{{Task}}", request.Spec?.Task ?? "(no clarified task)"),
            ("{{DesiredOutcome}}", request.Spec?.DesiredOutcome ?? "(no clarified desired outcome)"),
            ("{{AcceptanceCriteriaSection}}", BuildStringListSection(request.Spec?.AcceptanceCriteria)),
            ("{{PlanStepsSection}}", BuildPlanStepsSection(request.Plan)),
            ("{{FilesTouchedSection}}", BuildStringListSection(request.FilesTouched)),
            ("{{BuildOutcomeSection}}", BuildBuildOutcomeSection(request.BuildOutcome)),
            ("{{VerificationEvidenceSection}}", BuildVerificationEvidenceSection(request.VerificationEvidence)),
            ("{{DeterministicChecksSection}}", BuildDeterministicChecksSection(deterministicResults)),
            ("{{ReviewSummarySection}}", BuildReviewSummarySection(request)));

        const int MAX_VERIFIER_ATTEMPTS = 3;
        string? lastResponse = null;
        for (int attempt = 1; attempt <= MAX_VERIFIER_ATTEMPTS; attempt++)
        {
            string previousResponsePreview = lastResponse?.Length > 1200 ? lastResponse[..1200] + "..." : lastResponse ?? string.Empty;
            string promptForAttempt = attempt == 1
                ? prompt
                : $"{prompt}\n\nIMPORTANT: Your previous response could not be parsed. Return ONLY the raw JSON object. No markdown, no code fences, no commentary.\nPrevious response:\n{previousResponsePreview}";

            lastResponse = await base.CopilotClient.CompleteAsync(
                model,
                promptForAttempt,
                options,
                agentId: agentId ?? base.Id,
                agentRole: agentRole ?? base.Role,
                cancellationToken).ConfigureAwait(false);

            if (TryParseImplementationAssessment(lastResponse, out ImplementationAssessment? assessment))
            {
                return assessment;
            }
        }

        return new ImplementationAssessment(
            "INCOMPLETE",
            false,
            "Verifier response could not be parsed, so material implementation could not be proven.",
            Array.Empty<string>(),
            new[] { "Verifier response could not be parsed." },
            new[] { "Final completion verdict is conservative because verifier proof is unavailable." });
    }

    private static string BuildValidationSummary(IReadOnlyList<CriterionResult> results, ImplementationAssessment assessment)
    {
        int failedCount = results.Count(result => !result.Passed);
        string baseSummary = failedCount == 0
            ? $"All {results.Count} completion criteria passed."
            : $"{failedCount} of {results.Count} completion criteria failed.";
        return $"{baseSummary} Material implementation verdict: {assessment.Verdict}. {assessment.Summary}";
    }

    private static string ResolveValidationConfidence(CompletionValidationRequest request, ImplementationAssessment assessment)
    {
        bool hasEvidence = request.VerificationEvidence is { Count: > 0 };
        bool hasVerifierEvidence = assessment.Evidence.Count > 0;
        if (hasEvidence && hasVerifierEvidence)
        {
            return "high";
        }

        if (hasEvidence || hasVerifierEvidence)
        {
            return "medium";
        }

        return "low";
    }

    private static string BuildImplementationEvidenceSummary(ImplementationAssessment assessment)
    {
        string gaps = assessment.Gaps.Count == 0 ? string.Empty : $" Gaps: {string.Join("; ", assessment.Gaps)}";
        return $"{assessment.Summary}{gaps}".Trim();
    }

    private static ExecutionPlan ApplyArchitectureLoopMode(ExecutionPlan plan, RunRequest request, ReviewLoopAgentSelection reviewLoopAgents)
    {
        if (!request.ArchitectureLoopMode)
        {
            return plan;
        }

        IterationStrategy loopIteration = new IterationStrategy(
            MaxIterations: Math.Max(2, plan.IterationStrategy.MaxIterations),
            ReviewRequired: reviewLoopAgents.AnyFindingReviewEnabled);

        IReadOnlyList<ExecutionPlanStep> updatedSteps = plan.Steps
            .Select(step => step.Agent is "Architecture" or "Security"
                ? step with { Objective = BuildArchitectureLoopObjective(step.Objective, request.ArchitectureLoopPrompt) }
                : step)
            .ToArray();

        return new ExecutionPlan(updatedSteps, loopIteration, plan.CompletionCriteria);
    }

    private static string BuildArchitectureLoopObjective(string objective, string? architectureLoopPrompt)
    {
        string baseObjective = string.IsNullOrWhiteSpace(objective)
            ? "Review and enforce architecture constraints over the entire workspace."
            : objective.Trim();

        string promptSection = string.IsNullOrWhiteSpace(architectureLoopPrompt)
            ? string.Empty
            : $"{Environment.NewLine}ArchitectureLoopPrompt: {architectureLoopPrompt.Trim()}";

        return $"""
            {baseObjective}{promptSection}
            """;
    }

    private static string ResolveTaskPrompt(string? inputTaskPrompt, bool architectureLoopMode)
    {
        if (!architectureLoopMode)
        {
            return inputTaskPrompt ?? string.Empty;
        }

        string defaultArchitectureLoopTaskPrompt = PromptLoader.Load(
            "Orchestration",
            "default-architecture-loop-task.md",
            DEFAULT_ARCH_LOOP_TASK_PROMPT_FALLBACK);

        return string.IsNullOrWhiteSpace(inputTaskPrompt)
            ? defaultArchitectureLoopTaskPrompt
            : inputTaskPrompt;
    }

    private static CopilotCompletionOptions CreateOrchestrationCompletionOptions()
    {
        string systemInstructions = PromptLoader.Load("Orchestration", "system.md", ORCHESTRATION_SYSTEM_INSTRUCTIONS_FALLBACK);
        return new CopilotCompletionOptions()
        {
            SystemMessage = systemInstructions,
            SystemMessageMode = CopilotSystemMessageMode.Append,
            ExcludedTools = new[] { "edit_file" }
        };
    }

    private static bool TryParseClarificationSpec(string? response, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ClarificationSpec? spec)
    {
        spec = null;
        if (string.IsNullOrWhiteSpace(response))
        {
            return false;
        }

        try
        {
            string? cleaned = ExecutionPlanParser.ExtractJson(response);
            if (cleaned is null)
            {
                return false;
            }

            using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(cleaned);
            System.Text.Json.JsonElement root = doc.RootElement;

            string task = root.TryGetProperty("task", out System.Text.Json.JsonElement taskEl) ? taskEl.GetString() ?? string.Empty : string.Empty;
            string desiredOutcome = root.TryGetProperty("desiredOutcome", out System.Text.Json.JsonElement outcomeEl) ? outcomeEl.GetString() ?? string.Empty : string.Empty;

            spec = new ClarificationSpec(
                Task: task,
                DesiredOutcome: desiredOutcome,
                InScope: ReadStringArray(root, "inScope"),
                OutOfScope: ReadStringArray(root, "outOfScope"),
                Constraints: ReadStringArray(root, "constraints"),
                Assumptions: ReadStringArray(root, "assumptions"),
                AcceptanceCriteria: ReadStringArray(root, "acceptanceCriteria"),
                LikelyTouchpoints: ReadStringArray(root, "likelyTouchpoints"),
                OpenQuestions: ReadStringArray(root, "openQuestions"),
                DecisionNotes: ReadStringArray(root, "decisionNotes"),
                VerificationCommands: ReadVerificationCommands(root));

            return !string.IsNullOrWhiteSpace(spec.Task);
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<string> ReadStringArray(System.Text.Json.JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out System.Text.Json.JsonElement arrayElement)
            || arrayElement.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return arrayElement.EnumerateArray()
            .Where(value => value.ValueKind == System.Text.Json.JsonValueKind.String)
            .Select(value => value.GetString()!)
            .ToArray();
    }

    private static bool TryParseImplementationAssessment(string? response, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ImplementationAssessment? assessment)
    {
        assessment = null;
        if (string.IsNullOrWhiteSpace(response))
        {
            return false;
        }

        try
        {
            string? cleaned = ExecutionPlanParser.ExtractJson(response);
            if (cleaned is null)
            {
                return false;
            }

            using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(cleaned);
            System.Text.Json.JsonElement root = document.RootElement;
            string verdict = root.TryGetProperty("verdict", out System.Text.Json.JsonElement verdictElement)
                ? verdictElement.GetString() ?? "INCOMPLETE"
                : "INCOMPLETE";
            bool materiallyImplemented = root.TryGetProperty("materiallyImplemented", out System.Text.Json.JsonElement materiallyImplementedElement)
                && materiallyImplementedElement.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False
                && materiallyImplementedElement.GetBoolean();
            string summary = root.TryGetProperty("summary", out System.Text.Json.JsonElement summaryElement)
                ? summaryElement.GetString() ?? string.Empty
                : string.Empty;

            assessment = new ImplementationAssessment(
                verdict.Trim(),
                materiallyImplemented,
                summary.Trim(),
                ReadStringArray(root, "evidence"),
                ReadStringArray(root, "gaps"),
                ReadStringArray(root, "risks"));

            return !string.IsNullOrWhiteSpace(assessment.Verdict);
        }
        catch
        {
            return false;
        }
    }

    private static string BuildPlanStepsSection(ExecutionPlan plan)
        => plan.Steps.Count == 0
            ? NONE_LABEL
            : string.Join(Environment.NewLine, plan.Steps.Select(step => $"- {step.Id}. [{step.Agent}] {step.Objective}"));

    private static string BuildBuildOutcomeSection(BuildOutcome? buildOutcome)
        => buildOutcome is null
            ? NONE_LABEL
            : $"Passed: {buildOutcome.Passed}; Summary: {buildOutcome.Summary}; Command: {buildOutcome.Command ?? NONE_LABEL}; ExitCode: {buildOutcome.ExitCode?.ToString() ?? NONE_LABEL}";

    private static string BuildVerificationEvidenceSection(IReadOnlyList<VerificationEvidence>? evidence)
        => evidence is not { Count: > 0 }
            ? NONE_LABEL
            : string.Join(Environment.NewLine, evidence.Select(item => $"- [{(item.Passed ? "PASS" : "FAIL")}] {item.Name} ({item.Type}) -> {item.Summary}"));

    private static string BuildDeterministicChecksSection(IReadOnlyList<CriterionResult> results)
        => results.Count == 0
            ? NONE_LABEL
            : string.Join(Environment.NewLine, results.Select(result => $"- [{(result.Passed ? "PASS" : "FAIL")}] {result.Criterion}: {result.Evidence}"));

    private static string BuildReviewSummarySection(CompletionValidationRequest request)
    {
        int architectureHighCount = request.Review.Findings.Count(f => string.Equals(f.Severity, "high", StringComparison.OrdinalIgnoreCase));
        int securityHighCount = request.SecurityReview.Findings.Count(f => string.Equals(f.Severity, "high", StringComparison.OrdinalIgnoreCase));
        return $"ArchitectureHighFindings: {architectureHighCount}{Environment.NewLine}SecurityHighFindings: {securityHighCount}";
    }

    private static string BuildStringListSection(IReadOnlyList<string>? values)
        => values is not { Count: > 0 }
            ? NONE_LABEL
            : string.Join(Environment.NewLine, values.Select(value => $"- {value}"));

    private static IReadOnlyList<VerificationCommand> ReadVerificationCommands(System.Text.Json.JsonElement element)
    {
        if (!element.TryGetProperty("verificationCommands", out System.Text.Json.JsonElement commandsElement)
            || commandsElement.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return Array.Empty<VerificationCommand>();
        }

        List<VerificationCommand> commands = new List<VerificationCommand>();
        foreach (System.Text.Json.JsonElement commandElement in commandsElement.EnumerateArray())
        {
            VerificationCommand? parsed = TryParseVerificationCommand(commandElement);
            if (parsed is not null)
            {
                commands.Add(parsed);
            }
        }

        return commands;
    }

    private static VerificationCommand? TryParseVerificationCommand(System.Text.Json.JsonElement commandElement)
    {
        if (commandElement.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return null;
        }

        string? name = commandElement.TryGetProperty("name", out System.Text.Json.JsonElement nameElement) ? nameElement.GetString() : null;
        string? command = commandElement.TryGetProperty("command", out System.Text.Json.JsonElement commandValueElement) ? commandValueElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        string evidenceType = commandElement.TryGetProperty("evidenceType", out System.Text.Json.JsonElement evidenceTypeElement)
            ? evidenceTypeElement.GetString() ?? "runtime"
            : "runtime";
        string? criterion = commandElement.TryGetProperty("criterion", out System.Text.Json.JsonElement criterionElement)
            ? criterionElement.GetString()
            : null;
        bool required = !commandElement.TryGetProperty("required", out System.Text.Json.JsonElement requiredElement)
            || requiredElement.ValueKind is not (System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)
            || requiredElement.GetBoolean();

        return new VerificationCommand(name.Trim(), command.Trim(), evidenceType.Trim(), criterion?.Trim(), required);
    }
}
