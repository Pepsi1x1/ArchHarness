using System.Text.Json;
using ArchHarness.App.Core;

namespace ArchHarness.App.Storage;

public sealed class ArtefactStore
{
    public Task WriteExecutionPlanAsync(string runDirectory, ExecutionPlan plan, CancellationToken cancellationToken)
        => File.WriteAllTextAsync(Path.Combine(runDirectory, "ExecutionPlan.json"), JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);

    public Task WriteArchitectureReviewAsync(string runDirectory, ArchitectureReview review, CancellationToken cancellationToken)
        => File.WriteAllTextAsync(Path.Combine(runDirectory, "ArchitectureReview.json"), JsonSerializer.Serialize(review, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);

    public Task WriteFinalSummaryAsync(string runDirectory, string summary, CancellationToken cancellationToken)
        => File.WriteAllTextAsync(Path.Combine(runDirectory, "FinalSummary.md"), summary, cancellationToken);

    public async Task AppendEventAsync(string runDirectory, object evt, CancellationToken cancellationToken)
    {
        var line = JsonSerializer.Serialize(evt);
        await File.AppendAllTextAsync(Path.Combine(runDirectory, "events.jsonl"), line + Environment.NewLine, cancellationToken);
    }
}
