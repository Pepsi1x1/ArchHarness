using System.Text.Json;
using System.Text.RegularExpressions;

namespace ArchHarness.App.Core;

/// <summary>
/// Parses and validates execution plan JSON into strongly-typed <see cref="ExecutionPlan"/> instances.
/// Owns schema validation, step normalization, and JSON extraction from raw model responses.
/// </summary>
public sealed class ExecutionPlanParser
{
    private const string FRONTEND_AGENT_NAME = "Frontend";
    private const string BUILDER_AGENT_NAME = "Builder";
    private const string STYLE_AGENT_NAME = "Style";
    private const string ARCHITECTURE_AGENT_NAME = "Architecture";

    private readonly IWorkspaceContextAnalyzer _workspaceContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExecutionPlanParser"/> class.
    /// </summary>
    /// <param name="workspaceContext">Analyzer for workspace language detection and objective enforcement.</param>
    public ExecutionPlanParser(IWorkspaceContextAnalyzer workspaceContext)
    {
        this._workspaceContext = workspaceContext;
    }

    /// <summary>
    /// Attempts to parse a raw model response into a validated <see cref="ExecutionPlan"/>.
    /// </summary>
    /// <param name="raw">The raw text response from the orchestration model.</param>
    /// <param name="workspaceRoot">The root path of the workspace used to enforce path constraints.</param>
    /// <param name="plan">When successful, the parsed execution plan.</param>
    /// <param name="validationError">When unsuccessful, a description of the validation failure.</param>
    /// <returns><c>true</c> if parsing and validation succeeded; otherwise <c>false</c>.</returns>
    public bool TryBuildExecutionPlan(string raw, string workspaceRoot, out ExecutionPlan plan, out string? validationError)
    {
        plan = default!;
        validationError = null;

        string? json = ExtractJson(raw);
        if (string.IsNullOrWhiteSpace(json))
        {
            validationError = "No JSON object found in response. Ensure response starts with '{' and ends with '}'";
            return false;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            if (!ValidatePlanSchema(root, out string? schemaError))
            {
                validationError = schemaError;
                return false;
            }

            if (!TryParseAndNormalizeSteps(root, workspaceRoot, out List<ExecutionPlanStep> steps, out string? stepError))
            {
                validationError = stepError;
                return false;
            }

            IterationStrategy iteration = ParseIterationStrategy(root);
            List<string> criteria = ParseCompletionCriteria(root);
            plan = new ExecutionPlan(steps, iteration, criteria);
            return true;
        }
        catch (JsonException ex)
        {
            validationError = $"JSON parse error: {ex.Message}";
            return false;
        }
        catch (Exception ex)
        {
            validationError = $"Unexpected error during plan parsing: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Validates the top-level schema of an execution plan JSON element.
    /// </summary>
    /// <param name="root">The root JSON element to validate.</param>
    /// <param name="error">When validation fails, a description of the error.</param>
    /// <returns><c>true</c> if the schema is valid; otherwise <c>false</c>.</returns>
    public static bool ValidatePlanSchema(JsonElement root, out string? error)
    {
        error = null;

        if (!root.TryGetProperty("steps", out JsonElement stepsEl))
        {
            error = "Missing required field: 'steps'";
            return false;
        }

        if (stepsEl.ValueKind != JsonValueKind.Array)
        {
            error = "Field 'steps' must be an array.";
            return false;
        }

        List<JsonElement> stepsArray = stepsEl.EnumerateArray().ToList();
        if (stepsArray.Count == 0)
        {
            error = "Field 'steps' array is empty. Must include at least 3 steps (Builder, Style, Architecture).";
            return false;
        }

        if (stepsArray.Count > 10)
        {
            error = $"Too many steps ({stepsArray.Count}). Maximum 6-8 steps recommended.";
            return false;
        }

        for (int i = 0; i < stepsArray.Count; i++)
        {
            JsonElement step = stepsArray[i];
            if (step.ValueKind != JsonValueKind.Object)
            {
                error = $"Step {i}: must be an object.";
                return false;
            }

            if (!step.TryGetProperty("agent", out JsonElement agentEl) || string.IsNullOrWhiteSpace(agentEl.GetString()))
            {
                error = $"Step {i}: missing or empty 'agent' field.";
                return false;
            }

            string? agentValue = agentEl.GetString()?.Trim().ToLowerInvariant();
            if (!new[] { "frontend", "builder", "implementation", "style", "coding-style", "standards", "architecture", "review" }
                .Contains(agentValue))
            {
                error = $"Step {i}: agent '{agentValue}' is not recognized. Use one of: Frontend, Builder, Style, Architecture.";
                return false;
            }

            if (!step.TryGetProperty("objective", out JsonElement objEl) || string.IsNullOrWhiteSpace(objEl.GetString()))
            {
                error = $"Step {i}: missing or empty 'objective' field.";
                return false;
            }

            if (step.TryGetProperty("dependsOn", out JsonElement depsEl) && depsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement dep in depsEl.EnumerateArray())
                {
                    if (!dep.TryGetInt32(out int depId) || depId <= 0)
                    {
                        error = $"Step {i}: dependsOn contains invalid ID. All dependency IDs must be positive integers (references to prior step IDs).";
                        return false;
                    }
                }
            }
        }

        if (!root.TryGetProperty("iterationStrategy", out JsonElement iterEl))
        {
            error = "Missing required field: 'iterationStrategy'";
            return false;
        }

        if (iterEl.ValueKind != JsonValueKind.Object)
        {
            error = "Field 'iterationStrategy' must be an object.";
            return false;
        }

        if (!root.TryGetProperty("completionCriteria", out JsonElement criteriaEl))
        {
            error = "Missing required field: 'completionCriteria'";
            return false;
        }

        if (criteriaEl.ValueKind != JsonValueKind.Array)
        {
            error = "Field 'completionCriteria' must be an array.";
            return false;
        }

        List<JsonElement> criteria = criteriaEl.EnumerateArray().ToList();
        if (criteria.Count == 0)
        {
            error = "Field 'completionCriteria' is empty. Must include at least one completion criterion.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Reorders execution plan steps so that Style and Architecture review steps follow
    /// all implementation steps, with correct dependency wiring.
    /// </summary>
    /// <param name="steps">The unordered list of execution plan steps.</param>
    /// <param name="workspaceLanguages">The detected workspace languages for fallback assignment.</param>
    /// <returns>The reordered steps with corrected IDs and dependencies.</returns>
    public List<ExecutionPlanStep> NormalizeStepOrdering(List<ExecutionPlanStep> steps, IReadOnlyList<string> workspaceLanguages)
    {
        List<ExecutionPlanStep> nonReview = steps.Where(s => s.Agent != ARCHITECTURE_AGENT_NAME && s.Agent != STYLE_AGENT_NAME).ToList();
        List<ExecutionPlanStep> style = steps
            .Where(s => s.Agent == STYLE_AGENT_NAME)
            .Where(s => this._workspaceContext.IsReviewObjective(s.Objective))
            .ToList();
        List<ExecutionPlanStep> architecture = steps
            .Where(s => s.Agent == ARCHITECTURE_AGENT_NAME)
            .Where(s => this._workspaceContext.IsReviewObjective(s.Objective))
            .ToList();

        if (nonReview.Count == 0)
        {
            return steps;
        }

        if (style.Count == 0)
        {
            style.Add(new ExecutionPlanStep(
                Id: -1,
                Agent: STYLE_AGENT_NAME,
                Objective: "Review completed implementation and enforce language coding standards and naming/style conventions; apply required corrections directly.",
                DependsOnStepIds: null,
                Languages: workspaceLanguages));
        }

        if (architecture.Count == 0)
        {
            architecture.Add(new ExecutionPlanStep(
                Id: -2,
                Agent: ARCHITECTURE_AGENT_NAME,
                Objective: "Review completed implementation and enforce SOLID/DRY/separation-of-concerns standards; apply required corrections directly.",
                DependsOnStepIds: null,
                Languages: workspaceLanguages));
        }

        ExecutionPlanStep finalStyle = style[^1] with
        {
            Languages = style[^1].Languages is { Count: > 0 }
                ? style[^1].Languages
                : workspaceLanguages
        };

        ExecutionPlanStep finalArchitecture = architecture[^1] with
        {
            Languages = architecture[^1].Languages is { Count: > 0 }
                ? architecture[^1].Languages
                : workspaceLanguages
        };

        List<ExecutionPlanStep> reordered = nonReview
            .Concat(new[]
            {
                finalStyle with { Id = -1, DependsOnStepIds = null },
                finalArchitecture with { Id = -2, DependsOnStepIds = null }
            })
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

        int styleIndex = reordered.FindLastIndex(s => s.Agent == STYLE_AGENT_NAME);
        if (styleIndex >= 0)
        {
            ExecutionPlanStep styleStep = reordered[styleIndex];
            int[] styleDepends = reordered
                .Where((_, index) => index < styleIndex)
                .Select(s => s.Id)
                .Distinct()
                .OrderBy(x => x)
                .ToArray();

            reordered[styleIndex] = styleStep with
            {
                DependsOnStepIds = styleDepends.Length > 0 ? styleDepends : null
            };
        }

        int architectureIndex = reordered.FindLastIndex(s => s.Agent == ARCHITECTURE_AGENT_NAME);
        if (architectureIndex >= 0)
        {
            ExecutionPlanStep architectureStep = reordered[architectureIndex];
            int styleStepId = styleIndex >= 0 ? reordered[styleIndex].Id : 0;
            int[] enforcedDepends = styleStepId > 0
                ? new[] { styleStepId }
                : reordered
                    .Where((_, index) => index < architectureIndex)
                    .Select(s => s.Id)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToArray();

            reordered[architectureIndex] = architectureStep with
            {
                DependsOnStepIds = enforcedDepends.Length > 0 ? enforcedDepends : null
            };
        }

        return reordered;
    }

    internal static string? NormalizeAgent(string raw)
    {
        if (raw.Equals("frontend", StringComparison.OrdinalIgnoreCase)) return FRONTEND_AGENT_NAME;
        if (raw.Equals("builder", StringComparison.OrdinalIgnoreCase) || raw.Equals("implementation", StringComparison.OrdinalIgnoreCase)) return BUILDER_AGENT_NAME;
        if (raw.Equals("style", StringComparison.OrdinalIgnoreCase) || raw.Equals("coding-style", StringComparison.OrdinalIgnoreCase) || raw.Equals("standards", StringComparison.OrdinalIgnoreCase)) return STYLE_AGENT_NAME;
        if (raw.Equals("architecture", StringComparison.OrdinalIgnoreCase) || raw.Equals("review", StringComparison.OrdinalIgnoreCase)) return ARCHITECTURE_AGENT_NAME;
        return null;
    }

    private bool TryParseAndNormalizeSteps(JsonElement root, string workspaceRoot, out List<ExecutionPlanStep> steps, out string? error)
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
            error = "Execution plan must include at least one Builder, one Style, and one Architecture step.";
            return false;
        }

        steps = this.NormalizeStepOrdering(steps, workspaceLanguages);
        return true;
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
        => steps.Any(s => s.Agent == BUILDER_AGENT_NAME)
        && steps.Any(s => s.Agent == STYLE_AGENT_NAME)
        && steps.Any(s => s.Agent == ARCHITECTURE_AGENT_NAME);

    private static IterationStrategy ParseIterationStrategy(JsonElement root)
    {
        IterationStrategy iteration = new IterationStrategy(MaxIterations: 2, ReviewRequired: true);
        if (!root.TryGetProperty("iterationStrategy", out JsonElement itEl))
        {
            return iteration;
        }

        int maxIterations = itEl.TryGetProperty("maxIterations", out JsonElement maxEl) && maxEl.TryGetInt32(out int val)
            ? Math.Clamp(val, 1, 8)
            : 2;
        bool reviewRequired = !itEl.TryGetProperty("reviewRequired", out JsonElement reviewEl) || reviewEl.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || reviewEl.GetBoolean();
        return new IterationStrategy(maxIterations, reviewRequired);
    }

    private static List<string> ParseCompletionCriteria(JsonElement root)
    {
        List<string> criteria = new List<string>
        {
            "No high severity coding style findings",
            "No high severity architecture findings",
            "Build passes (if command configured)"
        };

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
        EnsureCriteriaContains(criteria, "style", "No high severity coding style findings");
        EnsureCriteriaContains(criteria, "architecture", "No high severity architecture findings");
        EnsureCriteriaContains(criteria, "build", "Build passes (if command configured)");
        return criteria;
    }

    private static void EnsureCriteriaContains(ICollection<string> criteria, string token, string requiredCriterion)
    {
        if (!criteria.Any(c => c.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            criteria.Add(requiredCriterion);
        }
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

    internal static string? ExtractJson(string text)
    {
        Match fenceMatch = Regex.Match(text, @"```(?:json)?\s*\n?(\{[\s\S]*?\})\s*```", RegexOptions.IgnoreCase);
        if (fenceMatch.Success)
        {
            return fenceMatch.Groups[1].Value;
        }

        int start = text.IndexOf('{');
        int end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        return text[start..(end + 1)];
    }
}
