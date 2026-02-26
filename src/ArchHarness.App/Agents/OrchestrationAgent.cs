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
    private const string DEFAULT_ARCH_LOOP_TASK_PROMPT = "Run architecture and style review loop for the existing workspace and apply required remediation.";

    private const string ORCHESTRATION_SYSTEM_INSTRUCTIONS = """
        You are the orchestration planner.
        Your role is planning and delegation only.
        Never modify workspace files directly and never perform implementation work.
        Never invoke file editing tools, including edit_file, this is the delegated agents job.
        Produce delegated prompts and validation outputs for specialized agents.
        """;

    private static readonly CopilotCompletionOptions ORCHESTRATION_COMPLETION_OPTIONS = new CopilotCompletionOptions()
    {
        SystemMessage = ORCHESTRATION_SYSTEM_INSTRUCTIONS,
        SystemMessageMode = CopilotSystemMessageMode.Append,
        ExcludedTools = new[] { "edit_file" }
    };

    private readonly ExecutionPlanParser _executionPlanParser;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrchestrationAgent"/> class.
    /// </summary>
    public OrchestrationAgent(
        ICopilotClient copilotClient,
        IModelResolver modelResolver,
        IAgentToolPolicyProvider toolPolicyProvider,
        IOptions<AgentsOptions> agentsOptions,
        ExecutionPlanParser executionPlanParser)
        : base(copilotClient, modelResolver, toolPolicyProvider, agentsOptions, "orchestration", Guid.NewGuid().ToString("N"))
    {
        _executionPlanParser = executionPlanParser;
    }

    internal CopilotCompletionOptions GetWarmUpCompletionOptions()
        => base.ApplyToolPolicy(ORCHESTRATION_COMPLETION_OPTIONS);

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

        string planningPrompt = $$"""
            You are the orchestration planner. Return ONLY strict JSON with this schema:
            {
                "steps": [{"id":1,"agent":"Frontend|Builder|Style|Architecture","objective":"string","dependsOn":[1],"languages":["dotnet","vue3"]}],
                "iterationStrategy": {"maxIterations": 2, "reviewRequired": true},
                "completionCriteria": ["string"]
            }

            Constraints:
            - Always include at least one Builder, one Style, and one Architecture step.
            - Include Frontend when UI/UX work is implied.
            - Style and Architecture are review/enforcement steps.
            - Style must execute before Architecture.
            - Architecture must be a single final review/enforcement step only.
            - Never use Architecture for solution design/spec generation/planning.
            - Never use Style for solution design/spec generation/planning.
            - Use dependsOn to encode step dependencies when a step requires outputs from prior steps.
            - If a step has no dependencies, omit dependsOn or set it to []. Do NOT use 0.
            - Use languages on Style/Architecture steps to declare review scope (dotnet and/or vue3).
            - All filesystem paths in objectives must be under WorkspaceRoot.
            - Do not use directories relative to process CWD; always anchor to WorkspaceRoot.
            - Keep 3-6 steps total.
            - completionCriteria must include style, architecture, and build verification.
            - Each objective must be a concrete delegated prompt the target agent can execute directly.
            - If ArchitectureLoopMode is true, Architecture objective(s) must review and enforce over the entire WorkspaceRoot.

            TaskPrompt: {{effectiveTaskPrompt}}
            WorkspaceRoot: {{workspaceRoot}}
            WorkspaceMode: {{request.WorkspaceMode}}
            BuildCommand: {{buildCommand}}
            ArchitectureLoopMode: {{architectureLoopMode}}
            ArchitectureLoopPrompt: {{architectureLoopPrompt}}
            """;

        CopilotCompletionOptions options = base.ApplyToolPolicy(ORCHESTRATION_COMPLETION_OPTIONS);
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

            if (_executionPlanParser.TryBuildExecutionPlan(lastResponse, workspaceRoot, out ExecutionPlan parsedPlan, out lastValidationError))
            {
                return request.ArchitectureLoopMode
                    ? ApplyArchitectureLoopMode(parsedPlan, request, workspaceRoot)
                    : parsedPlan;
            }
        }

        string? preview = lastResponse?.Length > 500 ? lastResponse[..500] + "..." : lastResponse;
        throw new InvalidOperationException(
            $"Orchestration model did not return a valid ExecutionPlan JSON after {MAX_PLANNING_ATTEMPTS} attempts.\n" +
            $"Validation error: {lastValidationError}\n" +
            $"Last response preview: {preview}");
    }

    public async Task<string> BuildRemediationPromptAsync(
        RunRequest request,
        string workspaceRoot,
        ArchitectureReview review,
        int iteration,
        string? agentId = null,
        string? agentRole = null,
        CancellationToken cancellationToken = default)
    {
        string model = base.ResolveModel(request.ModelOverrides);
        string effectiveTaskPrompt = ResolveTaskPrompt(request.TaskPrompt, request.ArchitectureLoopMode);
        string reviewSummary = string.Join(Environment.NewLine, review.RequiredActions.Select(x => $"- {x}"));
        string requiredActionsSection = string.IsNullOrWhiteSpace(reviewSummary)
            ? string.Empty
            : $"{Environment.NewLine}RequiredActions:{Environment.NewLine}{reviewSummary}";

        string architectureLoopPromptSection = string.IsNullOrWhiteSpace(request.ArchitectureLoopPrompt)
            ? string.Empty
            : $"{Environment.NewLine}ArchitectureLoopPrompt:{Environment.NewLine}{request.ArchitectureLoopPrompt}";

        string prompt = $"""
            You are the orchestration planner.
            Generate a single delegated prompt for the Architecture agent.
            Focus only on remediation actions from architecture review.
            Return plain text only (no markdown, no JSON).

            Iteration: {iteration}
            OriginalTask: {effectiveTaskPrompt}
            WorkspaceRoot: {workspaceRoot}
            ArchitectureLoopMode: {request.ArchitectureLoopMode}
            {requiredActionsSection}
            {architectureLoopPromptSection}
            """;

        CopilotCompletionOptions options = base.ApplyToolPolicy(ORCHESTRATION_COMPLETION_OPTIONS);
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
            ? BuildArchitectureLoopObjective(remediationPrompt, workspaceRoot, request.ArchitectureLoopPrompt)
            : remediationPrompt;
    }

    public async Task<bool> ValidateCompletionAsync(
        CompletionValidationRequest request,
        string? agentId = null,
        string? agentRole = null,
        CancellationToken cancellationToken = default)
    {
        string model = base.ResolveModel(request.ModelOverrides);
        CopilotCompletionOptions options = base.ApplyToolPolicy(ORCHESTRATION_COMPLETION_OPTIONS);
        _ = await base.CopilotClient.CompleteAsync(
            model,
            "Validate completion",
            options,
            agentId: agentId ?? base.Id,
            agentRole: agentRole ?? base.Role,
            cancellationToken);

        bool hasHighFindings = request.Review.Findings.Any(f => string.Equals(f.Severity, "high", StringComparison.OrdinalIgnoreCase));
        bool buildRequired = request.BuildCommandConfigured && request.Plan.CompletionCriteria.Any(c => c.Contains("Build passes", StringComparison.OrdinalIgnoreCase));
        return !hasHighFindings && (!buildRequired || request.BuildPassed);
    }

    private static ExecutionPlan ApplyArchitectureLoopMode(ExecutionPlan plan, RunRequest request, string workspaceRoot)
    {
        if (!request.ArchitectureLoopMode)
        {
            return plan;
        }

        IterationStrategy loopIteration = new IterationStrategy(
            MaxIterations: Math.Max(2, plan.IterationStrategy.MaxIterations),
            ReviewRequired: true);

        IReadOnlyList<ExecutionPlanStep> updatedSteps = plan.Steps
            .Select(step => step.Agent == "Architecture"
                ? step with { Objective = BuildArchitectureLoopObjective(step.Objective, workspaceRoot, request.ArchitectureLoopPrompt) }
                : step)
            .ToArray();

        return new ExecutionPlan(updatedSteps, loopIteration, plan.CompletionCriteria);
    }

    private static string BuildArchitectureLoopObjective(string objective, string workspaceRoot, string? architectureLoopPrompt)
    {
        string baseObjective = string.IsNullOrWhiteSpace(objective)
            ? "Review and enforce architecture constraints over the entire workspace."
            : objective.Trim();

        string promptSection = string.IsNullOrWhiteSpace(architectureLoopPrompt)
            ? string.Empty
            : $"{Environment.NewLine}ArchitectureLoopPrompt: {architectureLoopPrompt.Trim()}";

        return $"""
            SessionMode: architecture-loop
            Scope: Review and enforce over the entire workspace at {workspaceRoot}
            {baseObjective}{promptSection}
            """;
    }

    private static string ResolveTaskPrompt(string? inputTaskPrompt, bool architectureLoopMode)
    {
        if (!architectureLoopMode)
        {
            return inputTaskPrompt ?? string.Empty;
        }

        return string.IsNullOrWhiteSpace(inputTaskPrompt)
            ? DEFAULT_ARCH_LOOP_TASK_PROMPT
            : inputTaskPrompt;
    }
}
