using ArchHarness.App.Core;
using ArchHarness.App.Copilot;

namespace ArchHarness.App.Agents;

/// <summary>
/// Orchestration agent responsible for planning execution steps, building remediation prompts,
/// and validating run completion.
/// </summary>
public sealed class OrchestrationAgent : AgentBase
{
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
    /// <param name="copilotClient">Client for Copilot completions.</param>
    /// <param name="modelResolver">Resolver for model identifiers.</param>
    /// <param name="toolPolicyProvider">Provider for agent tool access policies.</param>
    /// <param name="executionPlanParser">Parser for validating and building execution plans from JSON.</param>
    public OrchestrationAgent(ICopilotClient copilotClient, IModelResolver modelResolver, IAgentToolPolicyProvider toolPolicyProvider, ExecutionPlanParser executionPlanParser)
        : base(copilotClient, modelResolver, toolPolicyProvider, "orchestration", Guid.NewGuid().ToString("N"))
    {
        this._executionPlanParser = executionPlanParser;
    }

    internal CopilotCompletionOptions GetWarmUpCompletionOptions()
        => base.ApplyToolPolicy(ORCHESTRATION_COMPLETION_OPTIONS);

    /// <summary>
    /// Builds an execution plan by prompting the orchestration model and parsing the structured JSON response.
    /// </summary>
    /// <param name="request">The run request containing the task prompt and configuration.</param>
    /// <param name="workspaceRoot">The root path of the workspace.</param>
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

            TaskPrompt: {{request.TaskPrompt}}
            WorkspaceRoot: {{workspaceRoot}}
            WorkspaceMode: {{request.WorkspaceMode}}
            BuildCommand: {{buildCommand}}
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

            if (this._executionPlanParser.TryBuildExecutionPlan(lastResponse, workspaceRoot, out ExecutionPlan parsedPlan, out lastValidationError))
            {
                return parsedPlan;
            }
        }

        string? preview = lastResponse?.Length > 500 ? lastResponse[..500] + "..." : lastResponse;
        throw new InvalidOperationException(
            $"Orchestration model did not return a valid ExecutionPlan JSON after {MAX_PLANNING_ATTEMPTS} attempts.\n" +
            $"Validation error: {lastValidationError}\n" +
            $"Last response preview: {preview}");
    }

    /// <summary>
    /// Builds a remediation prompt for the Architecture agent based on review findings.
    /// </summary>
    /// <param name="request">The originating run request.</param>
    /// <param name="workspaceRoot">The root path of the workspace.</param>
    /// <param name="review">The architecture review containing findings to remediate.</param>
    /// <param name="iteration">The current remediation iteration number.</param>
    /// <param name="agentId">Optional agent identifier override.</param>
    /// <param name="agentRole">Optional agent role override.</param>
    /// <param name="cancellationToken">Token to signal cancellation.</param>
    /// <returns>The remediation prompt text.</returns>
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
        string reviewSummary = string.Join(Environment.NewLine, review.RequiredActions.Select(x => $"- {x}"));
        string requiredActionsSection = string.IsNullOrWhiteSpace(reviewSummary)
            ? string.Empty
            : $"{Environment.NewLine}RequiredActions:{Environment.NewLine}{reviewSummary}";
        string prompt = $"""
            You are the orchestration planner.
            Generate a single delegated prompt for the Architecture agent.
            Focus only on remediation actions from architecture review.
            Return plain text only (no markdown, no JSON).

            Iteration: {iteration}
            OriginalTask: {request.TaskPrompt}
            WorkspaceRoot: {workspaceRoot}
            {requiredActionsSection}
            """;

        CopilotCompletionOptions options = base.ApplyToolPolicy(ORCHESTRATION_COMPLETION_OPTIONS);
        string response = await base.CopilotClient.CompleteAsync(
            model,
            prompt,
            options,
            agentId: agentId ?? base.Id,
            agentRole: agentRole ?? base.Role,
            cancellationToken);
        return string.IsNullOrWhiteSpace(response)
            ? $"Enforce all architecture required actions for iteration {iteration} directly in workspace files and re-check SOLID/DRY compliance."
            : response.Trim();
    }

    /// <summary>
    /// Validates whether a run meets its completion criteria based on review findings and build results.
    /// </summary>
    /// <param name="request">The completion validation request containing plan, review, and build status.</param>
    /// <param name="agentId">Optional agent identifier override.</param>
    /// <param name="agentRole">Optional agent role override.</param>
    /// <param name="cancellationToken">Token to signal cancellation.</param>
    /// <returns><c>true</c> if the run is complete; otherwise <c>false</c>.</returns>
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

}
