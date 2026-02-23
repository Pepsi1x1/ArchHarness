using ArchHarness.App.Core;
using ArchHarness.App.Copilot;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ArchHarness.App.Agents;

public sealed class OrchestrationAgent : AgentBase
{
    public OrchestrationAgent(ICopilotClient copilotClient, IModelResolver modelResolver)
        : base(copilotClient, modelResolver, "orchestration") { }

    public async Task<ExecutionPlan> BuildExecutionPlanAsync(RunRequest request, string workspaceRoot, CancellationToken cancellationToken = default)
    {
        var model = ResolveModel(request.ModelOverrides);
        var buildCommand = request.BuildCommand ?? "(none)";
        var planningPrompt = $$"""
            You are the orchestration planner. Return ONLY strict JSON with this schema:
            {
              "steps": [{"id":1,"agent":"Frontend|Builder|Architecture","objective":"string","dependsOn":[0]}],
              "iterationStrategy": {"maxIterations": 2, "reviewRequired": true},
              "completionCriteria": ["string"]
            }

            Constraints:
            - Always include at least one Builder and one Architecture step.
            - Include Frontend when UI/UX work is implied.
            - Architecture must be a single final review/enforcement step only.
            - Never use Architecture for solution design/spec generation/planning.
            - Use dependsOn to encode step dependencies when a step requires outputs from prior steps.
            - All filesystem paths in objectives must be under WorkspaceRoot.
            - Do not use directories relative to process CWD; always anchor to WorkspaceRoot.
            - Keep 3-6 steps total.
            - completionCriteria must include architecture and build verification.
            - Each objective must be a concrete delegated prompt the target agent can execute directly.

            TaskPrompt: {{request.TaskPrompt}}
            WorkspaceRoot: {{workspaceRoot}}
            WorkspaceMode: {{request.WorkspaceMode}}
            BuildCommand: {{buildCommand}}
            """;

        var planningResponse = await CopilotClient.CompleteAsync(model, planningPrompt, cancellationToken);
        if (TryBuildExecutionPlan(planningResponse, workspaceRoot, out var parsedPlan))
        {
            return parsedPlan;
        }

        throw new InvalidOperationException("Orchestration model did not return a valid delegated ExecutionPlan JSON payload.");
    }

    public async Task<string> BuildRemediationPromptAsync(
        RunRequest request,
        string workspaceRoot,
        ArchitectureReview review,
        int iteration,
        CancellationToken cancellationToken = default)
    {
        var model = ResolveModel(request.ModelOverrides);
        var reviewSummary = string.Join(Environment.NewLine, review.RequiredActions.Select(x => $"- {x}"));
        var actionsText = string.IsNullOrWhiteSpace(reviewSummary) ? "- none" : reviewSummary;
        var prompt = $"""
            You are the orchestration planner.
            Generate a single delegated prompt for the Architecture agent.
            Focus only on remediation actions from architecture review.
            Return plain text only (no markdown, no JSON).

            Iteration: {iteration}
            OriginalTask: {request.TaskPrompt}
            WorkspaceRoot: {workspaceRoot}
            RequiredActions:
            {actionsText}
            """;

        var response = await CopilotClient.CompleteAsync(model, prompt, cancellationToken);
        return string.IsNullOrWhiteSpace(response)
            ? $"Enforce all architecture required actions for iteration {iteration} directly in workspace files and re-check SOLID/DRY compliance."
            : response.Trim();
    }

    public async Task<bool> ValidateCompletionAsync(
        ExecutionPlan plan,
        ArchitectureReview review,
        bool buildPassed,
        bool buildCommandConfigured,
        IDictionary<string, string>? modelOverrides,
        CancellationToken cancellationToken = default)
    {
        var model = ResolveModel(modelOverrides);
        _ = await CopilotClient.CompleteAsync(model, "Validate completion", cancellationToken);

        var hasHighFindings = review.Findings.Any(f => string.Equals(f.Severity, "high", StringComparison.OrdinalIgnoreCase));
        var buildRequired = buildCommandConfigured && plan.CompletionCriteria.Any(c => c.Contains("Build passes", StringComparison.OrdinalIgnoreCase));
        return !hasHighFindings && (!buildRequired || buildPassed);
    }

    private static bool TryBuildExecutionPlan(string raw, string workspaceRoot, out ExecutionPlan plan)
    {
        plan = default!;
        var json = ExtractJson(raw);
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("steps", out var stepsElement) || stepsElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var steps = new List<ExecutionPlanStep>();
            var idx = 1;
            foreach (var step in stepsElement.EnumerateArray())
            {
                var parsedId = step.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var idVal) ? idVal : idx;
                var agent = step.TryGetProperty("agent", out var agentEl) ? agentEl.GetString() : null;
                var objective = step.TryGetProperty("objective", out var objEl) ? objEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(agent) || string.IsNullOrWhiteSpace(objective))
                {
                    idx++;
                    continue;
                }

                var normalizedAgent = NormalizeAgent(agent);
                if (normalizedAgent is null)
                {
                    idx++;
                    continue;
                }

                var sanitizedObjective = EnforceWorkspaceRootInObjective(objective, workspaceRoot);
                var dependsOn = ParseDependsOn(step);
                steps.Add(new ExecutionPlanStep(parsedId, normalizedAgent, sanitizedObjective, dependsOn));
                idx++;
            }

            if (!steps.Any(s => s.Agent == "Builder") || !steps.Any(s => s.Agent == "Architecture"))
            {
                return false;
            }

            steps = NormalizeStepOrdering(steps);

            var iteration = new IterationStrategy(MaxIterations: 2, ReviewRequired: true);
            if (root.TryGetProperty("iterationStrategy", out var itEl))
            {
                var maxIterations = itEl.TryGetProperty("maxIterations", out var maxEl) && maxEl.TryGetInt32(out var val)
                    ? Math.Clamp(val, 1, 8)
                    : 2;
                var reviewRequired = !itEl.TryGetProperty("reviewRequired", out var reviewEl) || reviewEl.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                    || reviewEl.GetBoolean();
                iteration = new IterationStrategy(maxIterations, reviewRequired);
            }

            var criteria = new List<string>
            {
                "No high severity architecture findings",
                "Build passes (if command configured)"
            };

            if (root.TryGetProperty("completionCriteria", out var criteriaEl) && criteriaEl.ValueKind == JsonValueKind.Array)
            {
                var parsedCriteria = criteriaEl.EnumerateArray()
                    .Select(x => x.GetString())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Cast<string>()
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (parsedCriteria.Count > 0)
                {
                    criteria = parsedCriteria;
                    if (!criteria.Any(c => c.Contains("architecture", StringComparison.OrdinalIgnoreCase)))
                    {
                        criteria.Add("No high severity architecture findings");
                    }

                    if (!criteria.Any(c => c.Contains("build", StringComparison.OrdinalIgnoreCase)))
                    {
                        criteria.Add("Build passes (if command configured)");
                    }
                }
            }

            plan = new ExecutionPlan(steps, iteration, criteria);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? NormalizeAgent(string raw)
    {
        if (raw.Equals("frontend", StringComparison.OrdinalIgnoreCase)) return "Frontend";
        if (raw.Equals("builder", StringComparison.OrdinalIgnoreCase) || raw.Equals("implementation", StringComparison.OrdinalIgnoreCase)) return "Builder";
        if (raw.Equals("architecture", StringComparison.OrdinalIgnoreCase) || raw.Equals("review", StringComparison.OrdinalIgnoreCase)) return "Architecture";
        return null;
    }

    private static List<ExecutionPlanStep> NormalizeStepOrdering(List<ExecutionPlanStep> steps)
    {
        var nonArchitecture = steps.Where(s => s.Agent != "Architecture").ToList();
        var architecture = steps
            .Where(s => s.Agent == "Architecture")
            .Where(s => IsArchitectureReviewObjective(s.Objective))
            .ToList();

        if (nonArchitecture.Count == 0)
        {
            return steps;
        }

        if (architecture.Count == 0)
        {
            architecture.Add(new ExecutionPlanStep(
                Id: 0,
                Agent: "Architecture",
                Objective: "Review completed implementation and enforce SOLID/DRY/separation-of-concerns standards; apply required corrections directly.",
                DependsOnStepIds: null));
        }

        // Keep exactly one final architecture step.
        var finalArchitecture = architecture[^1];

        var reordered = nonArchitecture
            .Concat(new[] { finalArchitecture with { Id = 0, DependsOnStepIds = null } })
            .ToList();

        var idMap = reordered
            .Select((step, index) => new { oldId = step.Id, newId = index + 1 })
            .ToDictionary(x => x.oldId, x => x.newId);

        for (var i = 0; i < reordered.Count; i++)
        {
            var step = reordered[i];
            var remappedDepends = step.DependsOnStepIds?
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

        var architectureIndex = reordered.FindLastIndex(s => s.Agent == "Architecture");
        if (architectureIndex >= 0)
        {
            var architectureStep = reordered[architectureIndex];
            var enforcedDepends = reordered
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

    private static IReadOnlyList<int>? ParseDependsOn(JsonElement step)
    {
        if (!step.TryGetProperty("dependsOn", out var dependsEl) || dependsEl.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var deps = dependsEl.EnumerateArray()
            .Where(x => x.TryGetInt32(out _))
            .Select(x => x.GetInt32())
            .Where(x => x > 0)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        return deps.Length == 0 ? null : deps;
    }

    private static bool IsArchitectureReviewObjective(string objective)
    {
        if (string.IsNullOrWhiteSpace(objective))
        {
            return false;
        }

        var text = objective.ToLowerInvariant();
        var looksLikeDesign = text.Contains("design")
            || text.Contains("spec")
            || text.Contains("concept")
            || text.Contains("define")
            || text.Contains("namespace layout")
            || text.Contains("project structure");

        if (looksLikeDesign)
        {
            return false;
        }

        var looksLikeReview = text.Contains("review")
            || text.Contains("verify")
            || text.Contains("enforce")
            || text.Contains("validate")
            || text.Contains("audit");

        return looksLikeReview;
    }

    private static string EnforceWorkspaceRootInObjective(string objective, string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(objective))
        {
            return objective;
        }

        var normalizedRoot = Path.GetFullPath(workspaceRoot).TrimEnd('\\', '/');
        const string windowsPathPattern = "(?<![A-Za-z0-9_])([A-Za-z]:\\\\[^\\s\\\"']+)";

        return Regex.Replace(objective, windowsPathPattern, match =>
        {
            var originalPath = match.Groups[1].Value;
            try
            {
                var full = Path.GetFullPath(originalPath).TrimEnd('\\', '/');
                if (full.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return originalPath;
                }

                return normalizedRoot;
            }
            catch
            {
                return normalizedRoot;
            }
        });
    }

    private static string? ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        return text[start..(end + 1)];
    }
}
