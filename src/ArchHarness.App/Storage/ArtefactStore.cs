using System.Text.Json;
using ArchHarness.App.Core;

namespace ArchHarness.App.Storage;

public interface IArtefactStore
{
    Task WriteExecutionPlanAsync(string runDirectory, ExecutionPlan plan, CancellationToken cancellationToken);
    Task WriteArchitectureReviewAsync(string runDirectory, ArchitectureReview review, CancellationToken cancellationToken);
    Task WriteFinalSummaryAsync(string runDirectory, string summary, CancellationToken cancellationToken);
    Task WriteBuildResultAsync(string runDirectory, object payload, CancellationToken cancellationToken);
    Task AppendEventAsync(string runDirectory, object evt, CancellationToken cancellationToken);
}

public sealed class ArtefactStore : IArtefactStore
{
    public Task WriteExecutionPlanAsync(string runDirectory, ExecutionPlan plan, CancellationToken cancellationToken)
        => File.WriteAllTextAsync(Path.Combine(runDirectory, "ExecutionPlan.json"), JsonSerializer.Serialize(plan, JsonDefaults.Indented), cancellationToken);

    public Task WriteArchitectureReviewAsync(string runDirectory, ArchitectureReview review, CancellationToken cancellationToken)
        => File.WriteAllTextAsync(Path.Combine(runDirectory, "ArchitectureReview.json"), JsonSerializer.Serialize(review, JsonDefaults.Indented), cancellationToken);

    public Task WriteFinalSummaryAsync(string runDirectory, string summary, CancellationToken cancellationToken)
        => File.WriteAllTextAsync(Path.Combine(runDirectory, "FinalSummary.md"), summary, cancellationToken);

    public Task WriteBuildResultAsync(string runDirectory, object payload, CancellationToken cancellationToken)
        => File.WriteAllTextAsync(
            Path.Combine(runDirectory, "BuildResult.json"),
            Redaction.RedactSecrets(JsonSerializer.Serialize(payload, JsonDefaults.Indented)),
            cancellationToken);

    public async Task AppendEventAsync(string runDirectory, object evt, CancellationToken cancellationToken)
    {
        var line = Redaction.RedactSecrets(JsonSerializer.Serialize(evt));
        await File.AppendAllTextAsync(Path.Combine(runDirectory, "events.jsonl"), line + Environment.NewLine, cancellationToken);
    }
}
