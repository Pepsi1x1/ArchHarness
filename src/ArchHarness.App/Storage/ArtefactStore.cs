using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
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
    /// Appends a raw SDK event as a JSONL line to the dedicated SDK event log.
    /// </summary>
    /// <param name="runDirectory">The run output directory.</param>
    /// <param name="evt">The raw SDK event object to serialize and append.</param>
    /// <param name="cancellationToken">Token to signal cancellation.</param>
    Task AppendSdkEventAsync(string runDirectory, object evt, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the clarification spec as both JSON and Markdown to the run directory.
    /// </summary>
    Task WriteClarificationSpecAsync(string runDirectory, ClarificationSpec spec, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the plan approval decision to the run directory as JSON.
    /// </summary>
    Task WritePlanApprovalAsync(string runDirectory, PlanApproval approval, CancellationToken cancellationToken);

    /// <summary>
    /// Flushes any in-flight JSONL writes for the specified run directory and closes
    /// the backing writer tasks. Call once after the run completes (after event pumps stop).
    /// Safe to call even if no writes occurred for the run.
    /// </summary>
    /// <param name="runDirectory">The run output directory whose writers should be drained.</param>
    /// <param name="cancellationToken">Token to bound the wait for pending writes.</param>
    Task CompleteRunAsync(string runDirectory, CancellationToken cancellationToken);
}

/// <summary>
/// File-system-backed artefact store that persists run outputs as JSON and JSONL files.
/// </summary>
public sealed class ArtefactStore : IArtefactStore
{
    private const string NONE_LABEL = "(none)";

    // One writer per JSONL file path. Each writer owns an unbounded Channel<string>
    // and a single-reader pump task. Producers TryWrite non-blockingly; the pump
    // drains + flushes in batches. This eliminates the multi-writer interleave risk
    // on the file side and removes per-event FlushToDisk syscalls (WriteThrough dropped).
    private static readonly ConcurrentDictionary<string, JsonlAppendWriter> WRITERS = new(StringComparer.Ordinal);

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
    public Task AppendEventAsync(string runDirectory, object evt, CancellationToken cancellationToken)
    {
        EnqueueJsonLine(runDirectory, "events.jsonl", evt);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task AppendSdkEventAsync(string runDirectory, object evt, CancellationToken cancellationToken)
    {
        EnqueueJsonLine(runDirectory, "copilot-sdk-events.jsonl", evt);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task CompleteRunAsync(string runDirectory, CancellationToken cancellationToken)
    {
        string runDirectoryFull = Path.GetFullPath(runDirectory);
        List<JsonlAppendWriter> toComplete = new();
        string[] matchingKeys = WRITERS.Keys
            .Where(key => key.StartsWith(runDirectoryFull, StringComparison.Ordinal))
            .ToArray();
        foreach (string key in matchingKeys)
        {
            if (WRITERS.TryRemove(key, out JsonlAppendWriter? writer))
            {
                toComplete.Add(writer);
            }
        }
        foreach (JsonlAppendWriter writer in toComplete)
        {
            await writer.CompleteAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static void EnqueueJsonLine(string runDirectory, string fileName, object evt)
    {
        string line = Redaction.RedactSecrets(JsonSerializer.Serialize(evt));
        string eventsPath = Path.GetFullPath(Path.Combine(runDirectory, fileName));
        JsonlAppendWriter writer = WRITERS.GetOrAdd(eventsPath, static path => new JsonlAppendWriter(path));
        writer.Enqueue(line);
    }

    /// <summary>
    /// Single-reader channel-backed appender for a JSONL file. Producers call <see cref="Enqueue"/>
    /// which is non-blocking (unbounded channel). A background pump drains the channel in batches
    /// and flushes once per drained batch — natural adaptive batching without per-line syncs.
    /// </summary>
    private sealed class JsonlAppendWriter
    {
        private readonly string _path;
        private readonly Channel<string> _channel;
        private readonly Task _pumpTask;

        public JsonlAppendWriter(string path)
        {
            this._path = path;
            this._channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            });
            this._pumpTask = Task.Run(this.PumpAsync);
        }

        public void Enqueue(string line)
        {
            // Unbounded + never-completed-while-running: TryWrite only returns false after
            // the channel has been completed (run drained). Drop silently in that case —
            // telemetry is best-effort; the run is already shutting down.
            this._channel.Writer.TryWrite(line);
        }

        public async Task CompleteAsync(CancellationToken cancellationToken)
        {
            this._channel.Writer.TryComplete();
            Task awaitable = this._pumpTask;
            if (cancellationToken.CanBeCanceled)
            {
                Task cancelWait = Task.Delay(Timeout.Infinite, cancellationToken);
                Task finished = await Task.WhenAny(awaitable, cancelWait).ConfigureAwait(false);
                if (finished != awaitable)
                {
                    // Cancellation requested — do not await the pump; it will drain on
                    // its own and the FileStream finalizer / OS flush will persist buffers.
                    return;
                }
            }
            await awaitable.ConfigureAwait(false);
        }

        private async Task PumpAsync()
        {
            // Long-lived FileStream across batches. WriteThrough intentionally omitted:
            // JSONL telemetry does not need per-line durability; we rely on the pump's
            // batch flush and OS write-back cache.
            await using FileStream stream = new FileStream(
                this._path,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous);
            await using StreamWriter writer = new StreamWriter(stream);
            ChannelReader<string> reader = this._channel.Reader;

            try
            {
                while (await reader.WaitToReadAsync().ConfigureAwait(false))
                {
                    while (reader.TryRead(out string? line))
                    {
                        await writer.WriteLineAsync(line.AsMemory()).ConfigureAwait(false);
                    }
                    // One flush per drained batch: StreamWriter → FileStream buffers → OS.
                    // No FlushToDisk / fsync. Under high load batches grow and amortise the
                    // flush; under low load we flush roughly per-line, which is still cheap
                    // without WriteThrough.
                    await writer.FlushAsync().ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                // Swallow: telemetry writer must never crash the host. A final flush is
                // attempted by the using-dispose below.
            }
        }
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
        string assessment = RenderImplementationAssessment(validation.Assessment);
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

            ## Implementation Assessment
            {assessment}

            ## Criteria
            {criteria}

            ## Evidence
            {evidence}

            ## Attempts
            {attempts}
            """;
    }

    private static string RenderImplementationAssessment(ImplementationAssessment? assessment)
    {
        if (assessment is null)
        {
            return NONE_LABEL;
        }

        string evidence = assessment.Evidence.Count == 0 ? NONE_LABEL : string.Join("; ", assessment.Evidence);
        string gaps = assessment.Gaps.Count == 0 ? NONE_LABEL : string.Join("; ", assessment.Gaps);
        string risks = assessment.Risks.Count == 0 ? NONE_LABEL : string.Join("; ", assessment.Risks);
        return $"- Verdict: {assessment.Verdict}{Environment.NewLine}- MateriallyImplemented: {assessment.MateriallyImplemented}{Environment.NewLine}- Summary: {assessment.Summary}{Environment.NewLine}- Evidence: {evidence}{Environment.NewLine}- Gaps: {gaps}{Environment.NewLine}- Risks: {risks}";
    }
}
