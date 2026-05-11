using ArchHarness.App.Constants;
using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Agents;

/// <summary>
/// Planner agent responsible for initial Planning mode: clarification, chat-native plan revision,
/// and approved execution-plan generation before implementation handoff.
/// </summary>
public sealed class PlanningAgent : AgentBase
{
    private const string PLANNING_PROMPT_GROUP_NAME = "Planning";
    private const string NONE_LABEL = "(none)";

    private readonly IExecutionPlanParser _executionPlanParser;
    private readonly AgentsOptions _agentsOptions;
    private readonly IReviewLoopAgentSelectionAccessor _reviewLoopAgentSelectionAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlanningAgent"/> class.
    /// </summary>
    public PlanningAgent(
        ICopilotClient copilotClient,
        IModelResolver modelResolver,
        IAgentToolPolicyProvider toolPolicyProvider,
        IOptions<AgentsOptions> agentsOptions,
        IReviewLoopAgentSelectionAccessor reviewLoopAgentSelectionAccessor,
        IExecutionPlanParser executionPlanParser)
        : base(copilotClient, modelResolver, toolPolicyProvider, agentsOptions, "planning", Guid.NewGuid().ToString("N"))
    {
        this._agentsOptions = agentsOptions.Value;
        this._reviewLoopAgentSelectionAccessor = reviewLoopAgentSelectionAccessor;
        this._executionPlanParser = executionPlanParser;
    }

    /// <summary>
    /// Builds an initial execution plan for Planning mode handoff.
    /// </summary>
    public async Task<ExecutionPlan> BuildExecutionPlanAsync(
        RunRequest request,
        string workspaceRoot,
        PlanningContext? planningContext = null,
        string? agentId = null,
        string? agentRole = null,
        CancellationToken cancellationToken = default)
    {
        string model = base.ResolveModel(request.ModelOverrides);
        bool architectureLoopMode = request.ArchitectureLoopMode;
        string effectiveTaskPrompt = ResolveTaskPrompt(request.TaskPrompt, architectureLoopMode);
        ReviewLoopAgentSelection reviewLoopAgents = request.ReviewLoopAgents
            ?? this._reviewLoopAgentSelectionAccessor.Current
            ?? this._agentsOptions.GetReviewLoopAgentSelection();

        string planningPrompt = planningContext?.UseFollowUpOnlyPrompt == true
            ? BuildFollowUpOnlyPrompt(planningContext)
            : BuildPlanningPrompt(
                request,
                planningContext,
                workspaceRoot,
                effectiveTaskPrompt,
                reviewLoopAgents);

        CopilotCompletionOptions options = base.ApplyToolPolicy(CreatePlanningCompletionOptions());
        const int MAX_PLANNING_ATTEMPTS = 3;
        string? lastResponse = null;
        string? lastValidationError = null;

        for (int attempt = 1; attempt <= MAX_PLANNING_ATTEMPTS; attempt++)
        {
            string promptForAttempt = attempt == 1
                ? planningPrompt
                : BuildValidationFollowUpPrompt(lastValidationError);

            lastResponse = await base.CopilotClient.CompleteAsync(
                model,
                promptForAttempt,
                options,
                agentId: agentId ?? base.Id,
                agentRole: agentRole ?? base.Role,
                cancellationToken).ConfigureAwait(false);

            if (this._executionPlanParser.TryBuildExecutionPlan(lastResponse, workspaceRoot, out ExecutionPlan parsedPlan, out lastValidationError))
            {
                return request.ArchitectureLoopMode
                    ? ApplyArchitectureLoopMode(parsedPlan, request, reviewLoopAgents)
                    : parsedPlan;
            }
        }

        string? preview = lastResponse?.Length > 500 ? lastResponse[..500] + "..." : lastResponse;
        throw new InvalidOperationException(
            $"Planning model did not return a valid ExecutionPlan JSON after {MAX_PLANNING_ATTEMPTS} attempts.\n" +
            $"Validation error: {lastValidationError}\n" +
            $"Last response preview: {preview}");
    }

    /// <summary>
    /// Builds the clarification/spec artifact for an initial Planning mode conversation.
    /// </summary>
    public async Task<ClarificationSpec> BuildClarificationSpecAsync(
        RunRequest request,
        string workspaceRoot,
        IReadOnlyList<ClarificationAnswer>? clarificationAnswers = null,
        IReadOnlyList<ConversationMessage>? conversationHistory = null,
        string? agentId = null,
        string? agentRole = null,
        CancellationToken cancellationToken = default)
    {
        string model = base.ResolveModel(request.ModelOverrides);
        string buildCommand = request.BuildCommand ?? NONE_LABEL;

        string specTemplate = PromptLoader.Load(PLANNING_PROMPT_GROUP_NAME, "clarification-spec.md");
        string specPrompt = PromptLoader.Render(
            specTemplate,
            ("{{TaskPrompt}}", request.TaskPrompt),
            ("{{WorkspaceRoot}}", workspaceRoot),
            ("{{WorkspaceMode}}", request.WorkspaceMode),
            ("{{BuildCommand}}", buildCommand),
            ("{{ClarificationAnswersSection}}", BuildClarificationAnswersSection(clarificationAnswers)),
            ("{{ConversationHistorySection}}", BuildConversationHistorySection(conversationHistory)));

        CopilotCompletionOptions options = base.ApplyToolPolicy(CreatePlanningCompletionOptions());
        const int MAX_SPEC_ATTEMPTS = 3;
        string? lastResponse = null;

        for (int attempt = 1; attempt <= MAX_SPEC_ATTEMPTS; attempt++)
        {
            string promptForAttempt = attempt == 1
                ? specPrompt
                : BuildValidationFollowUpPrompt("Your previous response could not be parsed. Return the corrected raw JSON object.");

            lastResponse = await base.CopilotClient.CompleteAsync(
                model,
                promptForAttempt,
                options,
                agentId: agentId ?? base.Id,
                agentRole: agentRole ?? base.Role,
                cancellationToken).ConfigureAwait(false);

            if (TryParseClarificationSpec(lastResponse, out ClarificationSpec? spec))
            {
                return spec;
            }
        }

        string? responsePreview = lastResponse?.Length > 500 ? lastResponse[..500] + "..." : lastResponse;
        throw new InvalidOperationException(
            $"Planning model did not return a valid ClarificationSpec JSON after {MAX_SPEC_ATTEMPTS} attempts.\n" +
            $"Last response preview: {responsePreview}");
    }

    private static CopilotCompletionOptions CreatePlanningCompletionOptions()
    {
        string systemInstructions = PromptLoader.Load(PLANNING_PROMPT_GROUP_NAME, "system.md");
        return new CopilotCompletionOptions()
        {
            SystemMessage = systemInstructions,
            SystemMessageMode = CopilotSystemMessageMode.Append,
            ExcludedTools = new[] { "edit_file" }
        };
    }

    private static string BuildPlanningPrompt(
        RunRequest request,
        PlanningContext? planningContext,
        string workspaceRoot,
        string effectiveTaskPrompt,
        ReviewLoopAgentSelection reviewLoopAgents)
    {
        string buildCommand = request.BuildCommand ?? NONE_LABEL;
        bool architectureLoopMode = request.ArchitectureLoopMode;
        string architectureLoopPrompt = request.ArchitectureLoopPrompt ?? NONE_LABEL;
        string planningTemplate = PromptLoader.Load(PLANNING_PROMPT_GROUP_NAME, "planning.md");
        return PromptLoader.Render(
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
            ("{{ConversationHistorySection}}", BuildConversationHistorySection(planningContext?.ConversationHistory)),
            ("{{AttachmentContextSection}}", BuildAttachmentContextSection(planningContext?.Attachments ?? request.Attachments)),
            ("{{EnabledReviewLoopAgents}}", reviewLoopAgents.DescribeEnabledAgents()),
            ("{{DisabledReviewLoopAgents}}", reviewLoopAgents.DescribeDisabledAgents()),
            ("{{ReviewLoopCompletionCriteria}}", string.Join(Environment.NewLine, reviewLoopAgents.BuildCompletionCriteria().Select(criteria => $"- {criteria}"))));
    }

    private static string BuildFollowUpOnlyPrompt(PlanningContext planningContext)
    {
        if (planningContext.ConversationHistory is { Count: > 0 })
        {
            return string.Join(Environment.NewLine, planningContext.ConversationHistory.Select(FormatConversationLine));
        }

        if (!string.IsNullOrWhiteSpace(planningContext.PlanRevisionRequest))
        {
            return $"[{ConversationMessageKinds.PLAN_REVISION}] {ConversationRoles.USER}: {planningContext.PlanRevisionRequest.Trim()}";
        }

        return $"[{ConversationMessageKinds.PLAN_REVISION}] {ConversationRoles.USER}:";
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

    private static string BuildConversationHistorySection(IReadOnlyList<ConversationMessage>? history)
    {
        if (history is not { Count: > 0 })
        {
            return string.Empty;
        }

        List<string> lines = new(history.Count + 1) { "ConversationHistory:" };
        foreach (ConversationMessage message in history)
        {
            string authorLabel = string.IsNullOrWhiteSpace(message.AuthorAgent)
                ? message.Role
                : $"{message.Role}/{message.AuthorAgent}";
            string text = string.IsNullOrWhiteSpace(message.Text) ? string.Empty : message.Text.Trim();
            int attachmentCount = message.Attachments?.Count ?? 0;
            string attachmentSuffix = attachmentCount > 0 ? $" [+{attachmentCount} attachment(s)]" : string.Empty;
            lines.Add($"- [{message.Kind}] {authorLabel}: {text}{attachmentSuffix}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatConversationLine(ConversationMessage message)
    {
        string authorLabel = string.IsNullOrWhiteSpace(message.AuthorAgent)
            ? message.Role
            : $"{message.Role}/{message.AuthorAgent}";
        string text = string.IsNullOrWhiteSpace(message.Text) ? string.Empty : message.Text.Trim();
        int attachmentCount = message.Attachments?.Count ?? 0;
        string attachmentSuffix = attachmentCount > 0 ? $" [+{attachmentCount} attachment(s)]" : string.Empty;
        return $"[{message.Kind}] {authorLabel}: {text}{attachmentSuffix}";
    }

    private static string BuildAttachmentContextSection(IReadOnlyList<PromptAttachment>? attachments)
    {
        if (attachments is not { Count: > 0 })
        {
            return string.Empty;
        }

        List<string> lines = new(attachments.Count + 1) { "AttachmentContext:" };
        foreach (PromptAttachment attachment in attachments)
        {
            string fileName = string.IsNullOrWhiteSpace(attachment.FileName) ? "(unnamed)" : attachment.FileName!;
            string caption = string.IsNullOrWhiteSpace(attachment.Caption) ? string.Empty : $" - {attachment.Caption}";
            lines.Add($"- {attachment.Kind} {fileName} ({attachment.MimeType}, {attachment.SizeBytes} bytes){caption}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string JoinValues(IReadOnlyList<string> values)
        => values.Count == 0 ? NONE_LABEL : string.Join("; ", values);

    private static ExecutionPlan ApplyArchitectureLoopMode(ExecutionPlan plan, RunRequest request, ReviewLoopAgentSelection reviewLoopAgents)
    {
        if (!request.ArchitectureLoopMode)
        {
            return plan;
        }

        IterationStrategy loopIteration = new(
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
            "default-architecture-loop-task.md");

        return string.IsNullOrWhiteSpace(inputTaskPrompt)
            ? defaultArchitectureLoopTaskPrompt
            : inputTaskPrompt;
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

    private static IReadOnlyList<VerificationCommand> ReadVerificationCommands(System.Text.Json.JsonElement element)
    {
        if (!element.TryGetProperty("verificationCommands", out System.Text.Json.JsonElement commandsElement)
            || commandsElement.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return Array.Empty<VerificationCommand>();
        }

        List<VerificationCommand> commands = new();
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
