using System.Text.Json;

namespace ArchHarness.App.Storage;

public sealed class RunStore
{
    private readonly string _root;

    public RunStore(string workspaceRoot)
    {
        _root = Path.Combine(workspaceRoot, ".agent-harness", "runs");
    }

    public string CreateRunDirectory()
    {
        var runId = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssZ");
        var runDir = Path.Combine(_root, runId);
        Directory.CreateDirectory(runDir);
        return runDir;
    }

    public Task WriteRunLogAsync(string runDirectory, object payload, CancellationToken cancellationToken)
        => File.WriteAllTextAsync(Path.Combine(runDirectory, "run-log.json"), JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
}
