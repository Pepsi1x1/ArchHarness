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
    /// Appends an event as a JSONL line to the run events log.
    /// </summary>
    /// <param name="runDirectory">The run output directory.</param>
    /// <param name="evt">The event object to serialize and append.</param>
    /// <param name="cancellationToken">Token to signal cancellation.</param>
    Task AppendEventAsync(string runDirectory, object evt, CancellationToken cancellationToken);
}

/// <summary>
/// File-system-backed artefact store that persists run outputs as JSON and JSONL files.
/// </summary>
public sealed class ArtefactStore : IArtefactStore
{
    /// <inheritdoc />
    public Task WriteExecutionPlanAsync(string runDirectory, ExecutionPlan plan, CancellationToken cancellationToken)
        => File.WriteAllTextAsync(Path.Combine(runDirectory, "ExecutionPlan.json"), JsonSerializer.Serialize(plan, JsonDefaults.Indented), cancellationToken);

    /// <inheritdoc />
    public Task WriteArchitectureReviewAsync(string runDirectory, ArchitectureReview review, CancellationToken cancellationToken)
        => File.WriteAllTextAsync(Path.Combine(runDirectory, "ArchitectureReview.json"), JsonSerializer.Serialize(review, JsonDefaults.Indented), cancellationToken);

    /// <inheritdoc />
    public Task WriteFinalSummaryAsync(string runDirectory, string summary, CancellationToken cancellationToken)
        => File.WriteAllTextAsync(Path.Combine(runDirectory, "FinalSummary.md"), summary, cancellationToken);

    /// <inheritdoc />
    public Task WriteBuildResultAsync(string runDirectory, object payload, CancellationToken cancellationToken)
        => File.WriteAllTextAsync(
            Path.Combine(runDirectory, "BuildResult.json"),
            Redaction.RedactSecrets(JsonSerializer.Serialize(payload, JsonDefaults.Indented)),
            cancellationToken);

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
}
