using System.Text.Json;
using ArchHarness.App.Core;

namespace ArchHarness.App.Storage;

public interface IRunStore
{
    string CreateRunDirectory(string workspaceRoot);
    Task WriteRunLogAsync(string runDirectory, object payload, CancellationToken cancellationToken);
}

public sealed class RunStore : IRunStore
{
    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

    public string CreateRunDirectory(string workspaceRoot)
    {
        var root = Path.Combine(workspaceRoot, ".agent-harness", "runs");
        var runId = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfff");
        var runDir = Path.Combine(root, runId);
        Directory.CreateDirectory(runDir);
        return runDir;
    }

    public Task WriteRunLogAsync(string runDirectory, object payload, CancellationToken cancellationToken)
    {
        var serialized = JsonSerializer.Serialize(payload, IndentedJsonOptions);
        var redacted = Redaction.RedactSecrets(serialized);
        return File.WriteAllTextAsync(Path.Combine(runDirectory, "run-log.json"), redacted, cancellationToken);
    }
}
