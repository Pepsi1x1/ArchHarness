using ArchHarness.App.Constants;

namespace ArchHarness.App.Core;

/// <summary>
/// Snapshot of runtime state handed to a continuation planner after a wave completes.
/// </summary>
/// <param name="CurrentPlan">The plan as it currently stands (including any previously appended waves).</param>
/// <param name="RecentOutcomes">Outcomes from the most-recently completed wave.</param>
/// <param name="AllOutcomes">Append-only history of every <see cref="StepOutcome"/> recorded for the run.</param>
/// <param name="FilesTouched">Distinct files touched so far across the run.</param>
/// <param name="PreviousFilesTouchedCount">Count of touched files before the most-recent wave executed; used for no-change detection.</param>
/// <param name="NextWave">The wave number any newly appended steps should be stamped with.</param>
/// <param name="NextStepId">The next step id to allocate for appended steps.</param>
public sealed record ContinuationPlanningContext(
    ExecutionPlan CurrentPlan,
    IReadOnlyList<StepOutcome> RecentOutcomes,
    IReadOnlyList<StepOutcome> AllOutcomes,
    IReadOnlyList<string> FilesTouched,
    int PreviousFilesTouchedCount,
    int NextWave,
    int NextStepId);

/// <summary>
/// The outcome of a continuation-planning pass.
/// </summary>
/// <param name="NewSteps">Zero or more new steps to append. When empty, no follow-up wave should run.</param>
/// <param name="Reason">Human-readable reason describing why new steps were appended or why planning stopped.</param>
public sealed record ContinuationPlanningResult(
    IReadOnlyList<ExecutionPlanStep> NewSteps,
    string Reason);

/// <summary>
/// Plans additional execution-plan steps after a wave completes. The planner is deterministic and
/// never invents work on its own: it consumes structured <see cref="StepFollowUpHint"/> values
/// surfaced by the last wave's <see cref="StepOutcome"/> records and promotes them into new steps.
/// Safeguards (duplicate-signature detection, no-change detection, explicit completion) live in the
/// caller (<see cref="AgentStepExecutor"/>) to keep this component pure and easy to test.
/// </summary>
public interface IContinuationPlanner
{
    /// <summary>
    /// Produces the next continuation wave, if any.
    /// </summary>
    ContinuationPlanningResult PlanNextWave(ContinuationPlanningContext context);
}

/// <summary>
/// Default <see cref="IContinuationPlanner"/> implementation.
/// </summary>
public sealed class DeterministicContinuationPlanner : IContinuationPlanner
{
    /// <inheritdoc />
    public ContinuationPlanningResult PlanNextWave(ContinuationPlanningContext context)
    {
        if (context is null)
        {
            return new ContinuationPlanningResult(Array.Empty<ExecutionPlanStep>(), "no-context");
        }

        // Collect follow-up hints from the most-recent wave only. The executor tracks history and
        // safeguards so we do not re-emit the same step twice.
        List<StepFollowUpHint> hints = context.RecentOutcomes
            .Where(outcome => outcome.FollowUpHints is { Count: > 0 })
            .SelectMany(outcome => outcome.FollowUpHints!)
            .Where(hint => hint is not null && IsSupportedAgent(hint.Agent) && !string.IsNullOrWhiteSpace(hint.Objective))
            .ToList();

        if (hints.Count == 0)
        {
            return new ContinuationPlanningResult(Array.Empty<ExecutionPlanStep>(), "no-hints");
        }

        HashSet<string> existingSignatures = new HashSet<string>(
            context.CurrentPlan.Steps.Select(BuildSignature),
            StringComparer.OrdinalIgnoreCase);

        List<ExecutionPlanStep> appended = new List<ExecutionPlanStep>();
        int nextId = context.NextStepId;
        foreach (StepFollowUpHint hint in hints)
        {
            string signature = BuildSignatureFromHint(hint);
            if (!existingSignatures.Add(signature))
            {
                // Duplicate (agent, objective) — skip.
                continue;
            }

            ExecutionPlanStep step = new ExecutionPlanStep(
                Id: nextId++,
                Agent: hint.Agent,
                Objective: hint.Objective,
                DependsOnStepIds: null,
                Languages: hint.Languages,
                ParallelGroup: 1,
                Attachments: null,
                Wave: context.NextWave,
                OriginHint: string.IsNullOrWhiteSpace(hint.Reason) ? "continuation" : $"continuation:{hint.Reason}");
            appended.Add(step);
        }

        if (appended.Count == 0)
        {
            return new ContinuationPlanningResult(Array.Empty<ExecutionPlanStep>(), "hints-were-duplicates");
        }

        return new ContinuationPlanningResult(appended, $"appended-{appended.Count}-from-hints");
    }

    private static string BuildSignature(ExecutionPlanStep step)
        => $"{step.Agent}::{NormalizeObjective(step.Objective)}";

    private static string BuildSignatureFromHint(StepFollowUpHint hint)
        => $"{hint.Agent}::{NormalizeObjective(hint.Objective)}";

    private static string NormalizeObjective(string? objective)
        => (objective ?? string.Empty).Trim().ToLowerInvariant();

    private static bool IsSupportedAgent(string agent)
        => agent is AgentNames.FRONTEND_DEVELOPER
            or AgentNames.BACKEND_DEVELOPER
            or AgentNames.BUILD
            or AgentNames.CODING_STYLE
            or AgentNames.SECURITY
            or AgentNames.ARCHITECTURE;
}
