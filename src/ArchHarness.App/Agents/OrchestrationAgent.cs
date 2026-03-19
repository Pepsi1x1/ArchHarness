using ArchHarness.App.Constants;
using ArchHarness.App.Core;
using ArchHarness.App.Copilot;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Agents;

/// <summary>
/// Orchestration agent responsible for planning execution steps, building remediation prompts,
/// and validating run completion.
/// </summary>
public sealed class OrchestrationAgent : AgentBase
{
    private const string OrchestrationPromptGroupName = "Orchestration";
    private const string DEFAULT_ARCH_LOOP_TASK_PROMPT_FALLBACK = DefaultPrompts.ARCHITECTURE_LOOP_TASK;

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

        TaskPrompt: {{TaskPrompt}}
        WorkspaceRoot: {{WorkspaceRoot}}
        WorkspaceMode: {{WorkspaceMode}}
        BuildCommand: {{BuildCommand}}
        ArchitectureLoopMode: {{ArchitectureLoopMode}}
        ArchitectureLoopPrompt: {{ArchitectureLoopPrompt}}
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
        : base(copilotClient, modelResolver, toolPolicyProvider, agentsOptions, "orchestration", Guid.NewGuid().ToString("N"))
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
        string? agentId = null,
        string? agentRole = null,
        CancellationToken cancellationToken = default)
    {
        string model = base.ResolveModel(request.ModelOverrides);
        string buildCommand = request.BuildCommand ?? "(none)";
        bool architectureLoopMode = request.ArchitectureLoopMode;
        string architectureLoopPrompt = request.ArchitectureLoopPrompt ?? "(none)";
        string effectiveTaskPrompt = ResolveTaskPrompt(request.TaskPrompt, architectureLoopMode);
        ReviewLoopAgentSelection reviewLoopAgents = request.ReviewLoopAgents
            ?? this._reviewLoopAgentSelectionAccessor.Current
            ?? this._agentsOptions.GetReviewLoopAgentSelection();

        string planningTemplate = PromptLoader.Load(OrchestrationPromptGroupName, "planning.md", ORCHESTRATION_PLANNING_PROMPT_FALLBACK);
        string planningPrompt = PromptLoader.Render(
            planningTemplate,
            ("{{TaskPrompt}}", effectiveTaskPrompt),
            ("{{WorkspaceRoot}}", workspaceRoot),
            ("{{WorkspaceMode}}", request.WorkspaceMode),
            ("{{BuildCommand}}", buildCommand),
            ("{{ArchitectureLoopMode}}", architectureLoopMode.ToString()),
            ("{{ArchitectureLoopPrompt}}", architectureLoopPrompt),
            ("{{EnabledReviewLoopAgents}}", reviewLoopAgents.DescribeEnabledAgents()),
            ("{{DisabledReviewLoopAgents}}", reviewLoopAgents.DescribeDisabledAgents()),
            ("{{ReviewLoopCompletionCriteria}}", string.Join(Environment.NewLine, reviewLoopAgents.BuildCompletionCriteria().Select(x => $"- {x}"))));

        CopilotCompletionOptions options = base.ApplyToolPolicy(CreateOrchestrationCompletionOptions());
        const int MAX_PLANNING_ATTEMPTS = 3;
        string? lastResponse = null;
        string? lastValidationError = null;

        for (int attempt = 1; attempt <= MAX_PLANNING_ATTEMPTS; attempt++)
        {
            string promptForAttempt = attempt == 1
                ? planningPrompt
                : $"{planningPrompt}\n\nIMPORTANT: Your previous response could not be parsed. Return ONLY the raw JSON object. No markdown, no code fences, no commentary.";

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

        string remediationTemplate = PromptLoader.Load(OrchestrationPromptGroupName, "remediation.md", ORCHESTRATION_REMEDIATION_PROMPT_FALLBACK);
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
    /// Validates whether the run has met its completion criteria based on review findings and build status.
    /// </summary>
    /// <param name="request">The completion validation request containing the plan, reviews, and build results.</param>
    /// <param name="agentId">Optional agent identifier override.</param>
    /// <param name="agentRole">Optional agent role override.</param>
    /// <param name="cancellationToken">Token to signal cancellation.</param>
    /// <returns><see langword="true"/> if the run is complete with no high-severity findings and build passed; otherwise <see langword="false"/>.</returns>
    public async Task<bool> ValidateCompletionAsync(
        CompletionValidationRequest request,
        string? agentId = null,
        string? agentRole = null,
        CancellationToken cancellationToken = default)
    {
        string model = base.ResolveModel(request.ModelOverrides);
        CopilotCompletionOptions options = base.ApplyToolPolicy(CreateOrchestrationCompletionOptions());
        _ = await base.CopilotClient.CompleteAsync(
            model,
            "Validate completion",
            options,
            agentId: agentId ?? base.Id,
            agentRole: agentRole ?? base.Role,
            cancellationToken);

        ReviewLoopAgentSelection reviewLoopAgents = this._reviewLoopAgentSelectionAccessor.Current
            ?? this._agentsOptions.GetReviewLoopAgentSelection();
        bool hasHighFindings = reviewLoopAgents.ArchitectureEnabled
            && request.Review.Findings.Any(f => string.Equals(f.Severity, "high", StringComparison.OrdinalIgnoreCase));
        bool hasHighSecurityFindings = reviewLoopAgents.SecurityEnabled
            && request.SecurityReview.Findings.Any(f => string.Equals(f.Severity, "high", StringComparison.OrdinalIgnoreCase));
        return !hasHighFindings && !hasHighSecurityFindings;
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
}
