using System.Text.Json;
using System.Text.RegularExpressions;
using ArchHarness.App.Constants;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Core;

/// <summary>
/// Parses and validates execution plan JSON into strongly-typed <see cref="ExecutionPlan"/> instances.
/// Delegates schema validation to <see cref="ExecutionPlanValidator"/> and plan construction to <see cref="ExecutionPlanBuilder"/>.
/// </summary>
public sealed class ExecutionPlanParser : IExecutionPlanParser
{
    private readonly ExecutionPlanBuilder _builder;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExecutionPlanParser"/> class.
    /// </summary>
    /// <param name="workspaceContext">Analyzer for workspace language detection and objective enforcement.</param>
    /// <param name="agentsOptions">Optional agents configuration providing review loop defaults.</param>
    /// <param name="reviewLoopAgentSelectionAccessor">Optional per-request review loop selection override.</param>
    public ExecutionPlanParser(IWorkspaceContextAnalyzer workspaceContext, IOptions<AgentsOptions>? agentsOptions = null, IReviewLoopAgentSelectionAccessor? reviewLoopAgentSelectionAccessor = null)
    {
        ReviewLoopAgentSelection defaultReviewLoopAgents = (agentsOptions?.Value ?? new AgentsOptions()).GetReviewLoopAgentSelection();
        this._builder = new ExecutionPlanBuilder(workspaceContext, defaultReviewLoopAgents, reviewLoopAgentSelectionAccessor);
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
        ReviewLoopAgentSelection reviewLoopAgents = this._builder.GetCurrentReviewLoopAgents();

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

            if (!ExecutionPlanValidator.ValidatePlanSchema(root, out string? schemaError))
            {
                validationError = schemaError;
                return false;
            }

            if (!this._builder.TryBuildSteps(root, workspaceRoot, reviewLoopAgents, out List<ExecutionPlanStep> steps, out string? stepError))
            {
                validationError = stepError;
                return false;
            }

            IterationStrategy iteration = this._builder.BuildIterationStrategy(root, reviewLoopAgents);
            List<string> criteria = ExecutionPlanBuilder.BuildCompletionCriteria(root, reviewLoopAgents);
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
        => ExecutionPlanValidator.ValidatePlanSchema(root, out error);

    /// <summary>
    /// Reorders execution plan steps so that CodingStyle, Security, and Architecture review steps follow
    /// all implementation and build steps, injecting those default review steps when omitted.
    /// </summary>
    /// <param name="steps">The unordered list of execution plan steps.</param>
    /// <param name="workspaceLanguages">The detected workspace languages for fallback assignment.</param>
    /// <param name="reviewLoopAgents">Optional override of the active review loop selection.</param>
    /// <returns>The reordered steps with corrected IDs and dependencies.</returns>
    public List<ExecutionPlanStep> NormalizeStepOrdering(List<ExecutionPlanStep> steps, IReadOnlyList<string> workspaceLanguages, ReviewLoopAgentSelection? reviewLoopAgents = null)
        => this._builder.NormalizeStepOrdering(steps, workspaceLanguages, reviewLoopAgents);

    /// <summary>
    /// Normalizes a raw agent name string from the model response to its canonical form.
    /// </summary>
    /// <param name="raw">The raw agent name string.</param>
    /// <returns>The canonical agent name, or <c>null</c> if unrecognized.</returns>
    internal static string? NormalizeAgent(string raw) => ExecutionPlanBuilder.NormalizeAgent(raw);

    /// <summary>
    /// Extracts the first JSON object from a raw text string, supporting markdown code fences.
    /// </summary>
    /// <param name="text">The raw text potentially containing a JSON object.</param>
    /// <returns>The extracted JSON string, or <c>null</c> if no JSON object is found.</returns>
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
