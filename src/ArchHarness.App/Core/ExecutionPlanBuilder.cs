using System.Text.Json;
using ArchHarness.App.Constants;

namespace ArchHarness.App.Core;

/// <summary>
/// Constructs and normalizes execution plan objects from validated JSON elements.
/// Responsible for step mapping, ordering normalization, iteration strategy parsing,
/// and completion criteria resolution.
/// </summary>
internal sealed class ExecutionPlanBuilder
{
    private const string FRONTEND_DEVELOPER_AGENT_NAME = AgentNames.FRONTEND_DEVELOPER;
    private const string BACKEND_DEVELOPER_AGENT_NAME = AgentNames.BACKEND_DEVELOPER;
    private const string BUILD_AGENT_NAME = AgentNames.BUILD;
    private const string CODING_STYLE_AGENT_NAME = AgentNames.CODING_STYLE;
    private const string SECURITY_AGENT_NAME = AgentNames.SECURITY;
    private const string ARCHITECTURE_AGENT_NAME = AgentNames.ARCHITECTURE;

    private readonly IWorkspaceContextAnalyzer _workspaceContext;
    private readonly ReviewLoopAgentSelection _defaultReviewLoopAgents;
    private readonly IReviewLoopAgentSelectionAccessor? _reviewLoopAgentSelectionAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExecutionPlanBuilder"/> class.
    /// </summary>
    /// <param name="workspaceContext">Analyzer for workspace language detection and objective enforcement.</param>
    /// <param name="defaultReviewLoopAgents">The default review loop agent selection from configuration.</param>
    /// <param name="reviewLoopAgentSelectionAccessor">Optional per-request review loop selection override.</param>
    public ExecutionPlanBuilder(
        IWorkspaceContextAnalyzer workspaceContext,
        ReviewLoopAgentSelection defaultReviewLoopAgents,
        IReviewLoopAgentSelectionAccessor? reviewLoopAgentSelectionAccessor)
    {
        this._workspaceContext = workspaceContext;
        this._defaultReviewLoopAgents = defaultReviewLoopAgents;
        this._reviewLoopAgentSelectionAccessor = reviewLoopAgentSelectionAccessor;
    }

    /// <summary>
    /// Returns the currently active review loop agent selection, falling back to the configured default.
    /// </summary>
    public ReviewLoopAgentSelection GetCurrentReviewLoopAgents()
        => this._reviewLoopAgentSelectionAccessor?.Current ?? this._defaultReviewLoopAgents;

    /// <summary>
    /// Parses steps from the validated JSON root, normalizes them, and produces the final ordered list.
    /// </summary>
    /// <param name="root">The validated JSON root element.</param>
    /// <param name="workspaceRoot">The workspace root path for objective sanitization.</param>
    /// <param name="reviewLoopAgents">The active review loop agent selection.</param>
    /// <param name="steps">When successful, the normalized step list.</param>
    /// <param name="error">When unsuccessful, a description of the error.</param>
    /// <returns><c>true</c> if step construction and normalization succeeded; otherwise <c>false</c>.</returns>
    public bool TryBuildSteps(JsonElement root, string workspaceRoot, ReviewLoopAgentSelection reviewLoopAgents, out List<ExecutionPlanStep> steps, out string? error)
    {
        steps = new List<ExecutionPlanStep>();
        error = null;

        if (!root.TryGetProperty("steps", out JsonElement stepsElement) || stepsElement.ValueKind != JsonValueKind.Array)
        {
            error = "Required field 'steps' not found or is not an array.";
            return false;
        }

        IReadOnlyList<string> workspaceLanguages = this._workspaceContext.DetectWorkspaceLanguages(workspaceRoot);
        int index = 1;
        foreach (JsonElement step in stepsElement.EnumerateArray())
        {
            if (this.TryParseStep(step, workspaceRoot, index, out ExecutionPlanStep parsed))
            {
                steps.Add(parsed);
            }

            index++;
        }

        if (!ContainsRequiredAgents(steps))
        {
            error = "Execution plan must include at least one FrontendDeveloper, BackendDeveloper, or Build step. CodingStyle, Security, and Architecture review steps are injected by the harness when omitted.";
            return false;
        }

        steps = this.NormalizeStepOrdering(steps, workspaceLanguages, reviewLoopAgents);
        return true;
    }

    /// <summary>
    /// Reorders execution plan steps so that CodingStyle, Security, and Architecture review steps follow
    /// all implementation and build steps, injecting those default review steps when omitted.
    /// </summary>
    /// <param name="steps">The unordered list of execution plan steps.</param>
    /// <param name="workspaceLanguages">The detected workspace languages for fallback assignment.</param>
    /// <param name="reviewLoopAgents">Optional override of the active review loop selection.</param>
    /// <returns>The reordered steps with corrected IDs and dependencies.</returns>
    public List<ExecutionPlanStep> NormalizeStepOrdering(List<ExecutionPlanStep> steps, IReadOnlyList<string> workspaceLanguages, ReviewLoopAgentSelection? reviewLoopAgents = null)
    {
        reviewLoopAgents ??= this.GetCurrentReviewLoopAgents();

        List<ExecutionPlanStep> terminalValidationBuilds = steps
            .Where(IsTerminalValidationBuildStep)
            .ToList();
        List<ExecutionPlanStep> nonTerminalSteps = steps
            .Where(s => !IsTerminalValidationBuildStep(s))
            .ToList();
        List<ExecutionPlanStep> enabledReviewSteps = nonTerminalSteps
            .Where(s => IsReviewAgent(s.Agent) && reviewLoopAgents.IsEnabled(s.Agent))
            .ToList();
        List<ExecutionPlanStep> nonReview = nonTerminalSteps
            .Where(s => !IsReviewAgent(s.Agent))
            .ToList();
        List<ExecutionPlanStep> codingStyle = enabledReviewSteps
            .Where(s => s.Agent == CODING_STYLE_AGENT_NAME)
            .Where(s => this._workspaceContext.IsReviewObjective(s.Objective))
            .ToList();
        List<ExecutionPlanStep> security = enabledReviewSteps
            .Where(s => s.Agent == SECURITY_AGENT_NAME)
            .Where(s => this._workspaceContext.IsReviewObjective(s.Objective))
            .ToList();
        List<ExecutionPlanStep> architecture = enabledReviewSteps
            .Where(s => s.Agent == ARCHITECTURE_AGENT_NAME)
            .Where(s => this._workspaceContext.IsReviewObjective(s.Objective))
            .ToList();

        if (nonReview.Count == 0 && terminalValidationBuilds.Count == 0)
        {
            return steps;
        }

        if (reviewLoopAgents.CodingStyleEnabled && codingStyle.Count == 0)
        {
            codingStyle.Add(new ExecutionPlanStep(
                Id: -1,
                Agent: CODING_STYLE_AGENT_NAME,
                Objective: "Review completed implementation and enforce language coding standards and naming/style conventions; apply required corrections directly.",
                DependsOnStepIds: null,
                Languages: workspaceLanguages));
        }

        if (reviewLoopAgents.SecurityEnabled && security.Count == 0)
        {
            security.Add(new ExecutionPlanStep(
                Id: -2,
                Agent: SECURITY_AGENT_NAME,
                Objective: "Review completed implementation for security defects and OWASP Top 10 risks; apply required remediations directly.",
                DependsOnStepIds: null,
                Languages: workspaceLanguages));
        }

        if (reviewLoopAgents.ArchitectureEnabled && architecture.Count == 0)
        {
            architecture.Add(new ExecutionPlanStep(
                Id: -3,
                Agent: ARCHITECTURE_AGENT_NAME,
                Objective: "Review completed implementation and enforce SOLID/DRY/separation-of-concerns standards; apply required corrections directly.",
                DependsOnStepIds: null,
                Languages: workspaceLanguages));
        }

        List<ExecutionPlanStep> reviewSteps = new List<ExecutionPlanStep>();
        if (codingStyle.Count > 0)
        {
            reviewSteps.Add(codingStyle[^1] with
            {
                Languages = codingStyle[^1].Languages is { Count: > 0 }
                    ? codingStyle[^1].Languages
                    : workspaceLanguages,
                Id = -1,
                DependsOnStepIds = null
            });
        }

        if (security.Count > 0)
        {
            reviewSteps.Add(security[^1] with
            {
                Languages = security[^1].Languages is { Count: > 0 }
                    ? security[^1].Languages
                    : workspaceLanguages,
                Id = -2,
                DependsOnStepIds = null
            });
        }

        if (architecture.Count > 0)
        {
            reviewSteps.Add(architecture[^1] with
            {
                Languages = architecture[^1].Languages is { Count: > 0 }
                    ? architecture[^1].Languages
                    : workspaceLanguages,
                Id = -3,
                DependsOnStepIds = null
            });
        }

        List<ExecutionPlanStep> reordered = nonReview
            .Concat(reviewSteps)
            .Concat(terminalValidationBuilds.Select((step, index) => step with { Id = -100 - index, DependsOnStepIds = null }))
            .ToList();

        Dictionary<int, int> idMap = reordered
            .Select((step, index) => new { oldId = step.Id, newId = index + 1 })
            .ToDictionary(x => x.oldId, x => x.newId);

        for (int i = 0; i < reordered.Count; i++)
        {
            ExecutionPlanStep step = reordered[i];
            int[]? remappedDepends = step.DependsOnStepIds?
                .Where(dep => idMap.ContainsKey(dep))
                .Select(dep => idMap[dep])
                .Distinct()
                .OrderBy(dep => dep)
                .ToArray();

            reordered[i] = step with
            {
                Id = i + 1,
                DependsOnStepIds = remappedDepends is { Length: > 0 } ? remappedDepends : null
            };
        }

        int codingStyleIndex = reordered.FindLastIndex(s => s.Agent == CODING_STYLE_AGENT_NAME);
        if (codingStyleIndex >= 0)
        {
            ExecutionPlanStep codingStyleStep = reordered[codingStyleIndex];
            int[] codingStyleDepends = reordered
                .Where((_, index) => index < codingStyleIndex)
                .Select(s => s.Id)
                .Distinct()
                .OrderBy(x => x)
                .ToArray();

            reordered[codingStyleIndex] = codingStyleStep with
            {
                DependsOnStepIds = codingStyleDepends.Length > 0 ? codingStyleDepends : null
            };
        }

        int securityIndex = reordered.FindLastIndex(s => s.Agent == SECURITY_AGENT_NAME);
        if (securityIndex >= 0)
        {
            ExecutionPlanStep securityStep = reordered[securityIndex];
            int codingStyleStepId = codingStyleIndex >= 0 ? reordered[codingStyleIndex].Id : 0;
            int[] securityDepends = codingStyleStepId > 0
                ? new[] { codingStyleStepId }
                : reordered
                    .Where((_, index) => index < securityIndex)
                    .Select(s => s.Id)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToArray();

            reordered[securityIndex] = securityStep with
            {
                DependsOnStepIds = securityDepends.Length > 0 ? securityDepends : null
            };
        }

        int architectureIndex = reordered.FindLastIndex(s => s.Agent == ARCHITECTURE_AGENT_NAME);
        if (architectureIndex >= 0)
        {
            ExecutionPlanStep architectureStep = reordered[architectureIndex];
            int securityStepId = securityIndex >= 0 ? reordered[securityIndex].Id : 0;
            int codingStyleStepId = codingStyleIndex >= 0 ? reordered[codingStyleIndex].Id : 0;
            int[] enforcedDepends;
            if (securityStepId > 0)
            {
                enforcedDepends = new[] { securityStepId };
            }
            else if (codingStyleStepId > 0)
            {
                enforcedDepends = new[] { codingStyleStepId };
            }
            else
            {
                enforcedDepends = reordered
                    .Where((_, index) => index < architectureIndex)
                    .Select(s => s.Id)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToArray();
            }

            reordered[architectureIndex] = architectureStep with
            {
                DependsOnStepIds = enforcedDepends.Length > 0 ? enforcedDepends : null
            };
        }

        if (terminalValidationBuilds.Count > 0)
        {
            int previousDependencyId = architectureIndex >= 0 ? reordered[architectureIndex].Id : 0;
            int[] terminalBuildIndexes = reordered
                .Select((step, index) => new { step, index })
                .Where(x => IsTerminalValidationBuildStep(x.step))
                .Select(x => x.index)
                .ToArray();

            foreach (int buildIndex in terminalBuildIndexes)
            {
                ExecutionPlanStep buildStep = reordered[buildIndex];
                int[] enforcedDepends = previousDependencyId > 0 ? new[] { previousDependencyId } : Array.Empty<int>();
                reordered[buildIndex] = buildStep with
                {
                    DependsOnStepIds = enforcedDepends.Length > 0 ? enforcedDepends : null
                };
                previousDependencyId = reordered[buildIndex].Id;
            }
        }

        return reordered;
    }

    /// <summary>
    /// Parses and normalizes the iteration strategy from the validated JSON root.
    /// </summary>
    /// <param name="root">The validated JSON root element.</param>
    /// <param name="reviewLoopAgents">The active review loop agent selection.</param>
    /// <returns>The resolved <see cref="IterationStrategy"/>.</returns>
    public IterationStrategy BuildIterationStrategy(JsonElement root, ReviewLoopAgentSelection reviewLoopAgents)
    {
        IterationStrategy iteration = new IterationStrategy(MaxIterations: 2, ReviewRequired: true);
        if (!root.TryGetProperty("iterationStrategy", out JsonElement itEl))
        {
            return iteration with { ReviewRequired = iteration.ReviewRequired && reviewLoopAgents.AnyFindingReviewEnabled };
        }

        int maxIterations = itEl.TryGetProperty("maxIterations", out JsonElement maxEl) && maxEl.TryGetInt32(out int val)
            ? Math.Clamp(val, 1, 8)
            : 2;
        bool reviewRequired = !itEl.TryGetProperty("reviewRequired", out JsonElement reviewEl) || reviewEl.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || reviewEl.GetBoolean();
        return new IterationStrategy(maxIterations, reviewRequired && reviewLoopAgents.AnyFindingReviewEnabled);
    }

    /// <summary>
    /// Parses and normalizes the completion criteria from the validated JSON root.
    /// </summary>
    /// <param name="root">The validated JSON root element.</param>
    /// <param name="reviewLoopAgents">The active review loop agent selection.</param>
    /// <returns>The resolved list of completion criteria strings.</returns>
    public static List<string> BuildCompletionCriteria(JsonElement root, ReviewLoopAgentSelection reviewLoopAgents)
    {
        List<string> criteria = reviewLoopAgents.BuildCompletionCriteria().ToList();

        if (!root.TryGetProperty("completionCriteria", out JsonElement criteriaEl) || criteriaEl.ValueKind != JsonValueKind.Array)
        {
            return criteria;
        }

        List<string> parsedCriteria = criteriaEl.EnumerateArray()
            .Select(x => x.GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (parsedCriteria.Count == 0)
        {
            return criteria;
        }

        criteria = parsedCriteria;
        if (reviewLoopAgents.CodingStyleEnabled)
        {
            EnsureCriteriaContains(criteria, "coding style", "Coding style enforcement completed");
        }

        if (reviewLoopAgents.SecurityEnabled)
        {
            EnsureCriteriaContains(criteria, "security", "No high severity security findings");
        }

        if (reviewLoopAgents.ArchitectureEnabled)
        {
            EnsureCriteriaContains(criteria, "architecture", "No high severity architecture findings");
        }

        EnsureCriteriaContains(criteria, "build", "Build passes");
        return criteria;
    }

    /// <summary>
    /// Normalizes a raw agent name string from the model response to its canonical form.
    /// </summary>
    /// <param name="raw">The raw agent name string.</param>
    /// <returns>The canonical agent name, or <c>null</c> if unrecognized.</returns>
    internal static string? NormalizeAgent(string raw)
    {
        if (raw.Equals("frontenddeveloper", StringComparison.OrdinalIgnoreCase) || raw.Equals("frontend-developer", StringComparison.OrdinalIgnoreCase)) return FRONTEND_DEVELOPER_AGENT_NAME;
        if (raw.Equals("backenddeveloper", StringComparison.OrdinalIgnoreCase) || raw.Equals("backend-developer", StringComparison.OrdinalIgnoreCase)) return BACKEND_DEVELOPER_AGENT_NAME;
        if (raw.Equals("build", StringComparison.OrdinalIgnoreCase)) return BUILD_AGENT_NAME;
        if (raw.Equals("codingstyle", StringComparison.OrdinalIgnoreCase) || raw.Equals("coding-style", StringComparison.OrdinalIgnoreCase)) return CODING_STYLE_AGENT_NAME;
        if (raw.Equals("security", StringComparison.OrdinalIgnoreCase) || raw.Equals("secure", StringComparison.OrdinalIgnoreCase)) return SECURITY_AGENT_NAME;
        if (raw.Equals("architecture", StringComparison.OrdinalIgnoreCase) || raw.Equals("review", StringComparison.OrdinalIgnoreCase)) return ARCHITECTURE_AGENT_NAME;
        return null;
    }

    private bool TryParseStep(JsonElement step, string workspaceRoot, int fallbackId, out ExecutionPlanStep parsed)
    {
        parsed = default!;
        int parsedId = step.TryGetProperty("id", out JsonElement idEl) && idEl.TryGetInt32(out int idVal) ? idVal : fallbackId;
        string? agent = step.TryGetProperty("agent", out JsonElement agentEl) ? agentEl.GetString() : null;
        string? objective = step.TryGetProperty("objective", out JsonElement objEl) ? objEl.GetString() : null;

        if (string.IsNullOrWhiteSpace(agent) || string.IsNullOrWhiteSpace(objective))
        {
            return false;
        }

        string? normalizedAgent = NormalizeAgent(agent);
        if (normalizedAgent is null)
        {
            return false;
        }

        string sanitizedObjective = this._workspaceContext.EnforceWorkspaceRootInObjective(objective, workspaceRoot);
        parsed = new ExecutionPlanStep(parsedId, normalizedAgent, sanitizedObjective, ParseDependsOn(step), ParseLanguages(step));
        return true;
    }

    private static bool ContainsRequiredAgents(IEnumerable<ExecutionPlanStep> steps)
        => steps.Any(s => s.Agent == FRONTEND_DEVELOPER_AGENT_NAME
            || s.Agent == BACKEND_DEVELOPER_AGENT_NAME
            || s.Agent == BUILD_AGENT_NAME);

    private static bool IsReviewAgent(string agent)
        => agent == CODING_STYLE_AGENT_NAME || agent == SECURITY_AGENT_NAME || agent == ARCHITECTURE_AGENT_NAME;

    private static bool IsTerminalValidationBuildStep(ExecutionPlanStep step)
    {
        if (step.Agent != BUILD_AGENT_NAME)
        {
            return false;
        }

        string objective = step.Objective.Trim();
        if (objective.Length == 0)
        {
            return false;
        }

        return objective.Contains("final validation build", StringComparison.OrdinalIgnoreCase)
            || objective.Contains("validation build", StringComparison.OrdinalIgnoreCase)
            || objective.Contains("final build", StringComparison.OrdinalIgnoreCase)
            || (objective.Contains("confirm the build succeeds", StringComparison.OrdinalIgnoreCase)
                && objective.Contains("has not broken the build", StringComparison.OrdinalIgnoreCase))
            || (objective.Contains("confirm all remediation applied", StringComparison.OrdinalIgnoreCase)
                && objective.Contains("build", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<int>? ParseDependsOn(JsonElement step)
    {
        if (!step.TryGetProperty("dependsOn", out JsonElement dependsEl) || dependsEl.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        int[] deps = dependsEl.EnumerateArray()
            .Where(x => x.TryGetInt32(out _))
            .Select(x => x.GetInt32())
            .Where(x => x > 0)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        return deps.Length == 0 ? null : deps;
    }

    private static IReadOnlyList<string>? ParseLanguages(JsonElement step)
    {
        if (!step.TryGetProperty("languages", out JsonElement languagesEl) || languagesEl.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string[] languages = languagesEl.EnumerateArray()
            .Select(x => x.GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim().ToLowerInvariant())
            .Where(x => x is "dotnet" or "vue3")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return languages.Length == 0 ? null : languages;
    }

    private static void EnsureCriteriaContains(ICollection<string> criteria, string token, string requiredCriterion)
    {
        if (!criteria.Any(c => c.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            criteria.Add(requiredCriterion);
        }
    }
}
