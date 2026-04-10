using System.Text.Json;
using ArchHarness.App.Core;

namespace ArchHarness.App.Storage;

/// <summary>
/// Defines the contract for persisting run artefacts such as execution plans, reviews, and events.
/// </summary>
public interface IArtefactStore
{
    /// <summary>
    /// Writes the execution plan to the run directory as JSON.
    /// </summary>
    /// <param name="runDirectory">The run output directory.</param>
    /// <param name="plan">The execution plan to persist.</param>
    /// <param name="cancellationToken">Token to signal cancellation.</param>
    Task WriteExecutionPlanAsync(string runDirectory, ExecutionPlan plan, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the architecture review to the run directory as JSON.
    /// </summary>
    /// <param name="runDirectory">The run output directory.</param>
    /// <param name="review">The architecture review to persist.</param>
    /// <param name="cancellationToken">Token to signal cancellation.</param>
    Task WriteArchitectureReviewAsync(string runDirectory, ArchitectureReview review, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the security review to the run directory as JSON.
    /// </summary>
    /// <param name="runDirectory">The run output directory.</param>
    /// <param name="review">The security review to persist.</param>
    /// <param name="cancellationToken">Token to signal cancellation.</param>
    Task WriteSecurityReviewAsync(string runDirectory, SecurityReview review, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the final summary markdown to the run directory.
    /// </summary>
    /// <param name="runDirectory">The run output directory.</param>
    /// <param name="summary">The summary text to persist.</param>
    /// <param name="cancellationToken">Token to signal cancellation.</param>
    Task WriteFinalSummaryAsync(string runDirectory, string summary, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the build result to the run directory as JSON.
    /// </summary>
    /// <param name="runDirectory">The run output directory.</param>
    /// <param name="payload">The build result payload to persist.</param>
    /// <param name="cancellationToken">Token to signal cancellation.</param>
    Task WriteBuildResultAsync(string runDirectory, object payload, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the completion validation result to the run directory as JSON and Markdown.
    /// </summary>
    /// <param name="runDirectory">The run output directory.</param>
    /// <param name="validation">The validation result to persist.</param>
    /// <param name="cancellationToken">Token to signal cancellation.</param>
    Task WriteCompletionValidationAsync(string runDirectory, CompletionValidationResult validation, CancellationToken cancellationToken);

    /// <summary>
    /// Appends an event as a JSONL line to the run events log.
    /// </summary>
    /// <param name="runDirectory">The run output directory.</param>
    /// <param name="evt">The event object to serialize and append.</param>
    /// <param name="cancellationToken">Token to signal cancellation.</param>
    Task AppendEventAsync(string runDirectory, object evt, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the clarification spec as both JSON and Markdown to the run directory.
    /// </summary>
    Task WriteClarificationSpecAsync(string runDirectory, ClarificationSpec spec, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the plan approval decision to the run directory as JSON.
    /// </summary>
    Task WritePlanApprovalAsync(string runDirectory, PlanApproval approval, CancellationToken cancellationToken);
}

/// <summary>
/// File-system-backed artefact store that persists run outputs as JSON and JSONL files.
/// </summary>
public sealed class ArtefactStore : IArtefactStore
{
    private const string NONE_LABEL = "(none)";

    /// <inheritdoc />
    public Task WriteExecutionPlanAsync(string runDirectory, ExecutionPlan plan, CancellationToken cancellationToken)
    {
        return WriteRedactedJsonAsync(runDirectory, "ExecutionPlan.json", plan, cancellationToken);
    }

    /// <inheritdoc />
    public Task WriteArchitectureReviewAsync(string runDirectory, ArchitectureReview review, CancellationToken cancellationToken)
    {
        return WriteRedactedJsonAsync(runDirectory, "ArchitectureReview.json", review, cancellationToken);
    }

    /// <inheritdoc />
    public Task WriteSecurityReviewAsync(string runDirectory, SecurityReview review, CancellationToken cancellationToken)
    {
        return WriteRedactedJsonAsync(runDirectory, "SecurityReview.json", review, cancellationToken);
    }

    /// <inheritdoc />
    public Task WriteFinalSummaryAsync(string runDirectory, string summary, CancellationToken cancellationToken)
    {
        return WriteRedactedTextAsync(runDirectory, "FinalSummary.md", summary, cancellationToken);
    }

    /// <inheritdoc />
    public Task WriteBuildResultAsync(string runDirectory, object payload, CancellationToken cancellationToken)
        => WriteRedactedJsonAsync(runDirectory, "BuildResult.json", payload, cancellationToken);

    /// <inheritdoc />
    public async Task WriteCompletionValidationAsync(string runDirectory, CompletionValidationResult validation, CancellationToken cancellationToken)
    {
        await WriteRedactedJsonAsync(runDirectory, "CompletionValidation.json", validation, cancellationToken).ConfigureAwait(false);
        await WriteRedactedTextAsync(runDirectory, "CompletionValidation.md", RenderCompletionValidationMarkdown(validation), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task WriteClarificationSpecAsync(string runDirectory, ClarificationSpec spec, CancellationToken cancellationToken)
    {
        await WriteRedactedJsonAsync(runDirectory, "ClarificationSpec.json", spec, cancellationToken).ConfigureAwait(false);
        await WriteRedactedTextAsync(runDirectory, "ClarificationSpec.md", RenderSpecMarkdown(spec), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task WritePlanApprovalAsync(string runDirectory, PlanApproval approval, CancellationToken cancellationToken)
        => WriteRedactedJsonAsync(runDirectory, "PlanApproval.json", approval, cancellationToken);

    /// <inheritdoc />
    public async Task AppendEventAsync(string runDirectory, object evt, CancellationToken cancellationToken)
    {
        string line = Redaction.RedactSecrets(JsonSerializer.Serialize(evt));
        string eventsPath = Path.Combine(runDirectory, "events.jsonl");
        await using FileStream stream = new FileStream(
            eventsPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite,
            4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await using StreamWriter writer = new StreamWriter(stream);
        await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
    }

    private static Task WriteRedactedJsonAsync(string runDirectory, string fileName, object payload, CancellationToken cancellationToken)
    {
        string serialized = JsonSerializer.Serialize(payload, JsonDefaults.INDENTED);
        return WriteRedactedTextAsync(runDirectory, fileName, serialized, cancellationToken);
    }

    private static Task WriteRedactedTextAsync(string runDirectory, string fileName, string content, CancellationToken cancellationToken)
    {
        string filePath = Path.Combine(Path.GetFullPath(runDirectory), fileName);
        string redacted = Redaction.RedactSecrets(content);
        return File.WriteAllTextAsync(filePath, redacted, cancellationToken);
    }

    private static string RenderSpecMarkdown(ClarificationSpec spec)
    {
        static string RenderList(IReadOnlyList<string> items)
            => items.Count == 0 ? NONE_LABEL : string.Join(Environment.NewLine, items.Select(i => $"- {i}"));

        static string RenderVerificationCommands(IReadOnlyList<VerificationCommand>? commands)
            => commands is not { Count: > 0 }
                ? NONE_LABEL
                : string.Join(Environment.NewLine, commands.Select(command =>
                    $"- {command.Name}: `{command.Command}` ({command.EvidenceType}, criterion: {command.Criterion ?? command.Name}, required: {command.Required})"));

        return $"""
            # Clarification Spec

            ## Task
            {spec.Task}

            ## Desired Outcome
            {spec.DesiredOutcome}

            ## In Scope
            {RenderList(spec.InScope)}

            ## Out of Scope
            {RenderList(spec.OutOfScope)}

            ## Constraints
            {RenderList(spec.Constraints)}

            ## Assumptions
            {RenderList(spec.Assumptions)}

            ## Acceptance Criteria
            {RenderList(spec.AcceptanceCriteria)}

            ## Likely Touchpoints
            {RenderList(spec.LikelyTouchpoints)}

            ## Open Questions
            {RenderList(spec.OpenQuestions)}

            ## Decision Notes
            {RenderList(spec.DecisionNotes)}

            ## Verification Commands
            {RenderVerificationCommands(spec.VerificationCommands)}
            """;
    }

    private static string RenderCompletionValidationMarkdown(CompletionValidationResult validation)
    {
        string criteria = validation.CriterionResults.Count == 0
            ? NONE_LABEL
            : string.Join(Environment.NewLine, validation.CriterionResults.Select(result => $"- [{(result.Passed ? "PASS" : "FAIL")}] {result.Criterion}{Environment.NewLine}  Evidence: {result.Evidence}"));
        string evidence = validation.Evidence is not { Count: > 0 }
            ? NONE_LABEL
            : string.Join(Environment.NewLine, validation.Evidence.Select(item =>
                $"- [{(item.Passed ? "PASS" : "FAIL")}] {item.Name} ({item.Type}){Environment.NewLine}  Command: {item.Command}{Environment.NewLine}  ExitCode: {item.ExitCode}{Environment.NewLine}  Criterion: {item.Criterion ?? item.Name}{Environment.NewLine}  Summary: {item.Summary}"));
        string attempts = validation.Attempts is not { Count: > 0 }
            ? NONE_LABEL
            : string.Join(Environment.NewLine, validation.Attempts.Select(attempt =>
                $"- Attempt {attempt.AttemptNumber}: {(attempt.Passed ? "PASS" : "FAIL")} at {attempt.TimestampUtc:O}{Environment.NewLine}  Summary: {attempt.Summary}{Environment.NewLine}  RemediationPrompt: {attempt.RemediationPrompt ?? "(none)"}"));

        return $"""
            # Completion Validation

            - Passed: {validation.Passed}
            - Summary: {validation.Summary}
            - Confidence: {validation.Confidence}

            ## Criteria
            {criteria}

            ## Evidence
            {evidence}

            ## Attempts
            {attempts}
            """;
    }
}
