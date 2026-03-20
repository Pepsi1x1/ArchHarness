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
}
