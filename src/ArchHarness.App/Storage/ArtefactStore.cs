using System.Text.Json;
using ArchHarness.App.Core;

namespace ArchHarness.App.Storage;

public interface IArtefactStore
{
    Task WriteExecutionPlanAsync(string runDirectory, ExecutionPlan plan, CancellationToken cancellationToken);
    Task WriteArchitectureReviewAsync(string runDirectory, ArchitectureReview review, CancellationToken cancellationToken);
    Task WriteFinalSummaryAsync(string runDirectory, string summary, CancellationToken cancellationToken);
    Task AppendEventAsync(string runDirectory, object evt, CancellationToken cancellationToken);
}

public sealed class ArtefactStore : IArtefactStore
{
    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

    public Task WriteExecutionPlanAsync(string runDirectory, ExecutionPlan plan, CancellationToken cancellationToken)
        => File.WriteAllTextAsync(Path.Combine(runDirectory, "ExecutionPlan.json"), JsonSerializer.Serialize(plan, IndentedJsonOptions), cancellationToken);

    public Task WriteArchitectureReviewAsync(string runDirectory, ArchitectureReview review, CancellationToken cancellationToken)
        => File.WriteAllTextAsync(Path.Combine(runDirectory, "ArchitectureReview.json"), JsonSerializer.Serialize(review, IndentedJsonOptions), cancellationToken);

    public Task WriteFinalSummaryAsync(string runDirectory, string summary, CancellationToken cancellationToken)
        => File.WriteAllTextAsync(Path.Combine(runDirectory, "FinalSummary.md"), summary, cancellationToken);

    public async Task AppendEventAsync(string runDirectory, object evt, CancellationToken cancellationToken)
    {
        var line = JsonSerializer.Serialize(evt);
        await File.AppendAllTextAsync(Path.Combine(runDirectory, "events.jsonl"), line + Environment.NewLine, cancellationToken);
    }
}
