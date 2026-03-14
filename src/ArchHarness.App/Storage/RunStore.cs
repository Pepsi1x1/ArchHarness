using System.Text.Json;
using ArchHarness.App.Core;

namespace ArchHarness.App.Storage;

/// <summary>
/// Defines storage operations for creating and persisting run artifacts.
/// </summary>
public interface IRunStore
{
    /// <summary>
    /// Creates a timestamped run directory under the workspace root.
    /// </summary>
    /// <param name="workspaceRoot">The root path of the workspace.</param>
    /// <returns>The full path to the newly created run directory.</returns>
    string CreateRunDirectory(string workspaceRoot);

    /// <summary>
    /// Serializes a payload to a redacted JSON run log file in the specified run directory.
    /// </summary>
    /// <param name="runDirectory">The directory where the run log file will be written.</param>
    /// <param name="payload">The object to serialize as the run log.</param>
    /// <param name="cancellationToken">Token to signal cancellation.</param>
    /// <returns>A task representing the asynchronous write operation.</returns>
    Task WriteRunLogAsync(string runDirectory, object payload, CancellationToken cancellationToken);
}

/// <summary>
/// Persists run artifacts to the local file system, including timestamped run directories and redacted JSON logs.
/// </summary>
public sealed class RunStore : IRunStore
{
    /// <summary>
    /// Creates a timestamped run directory under the workspace root.
    /// </summary>
    /// <param name="workspaceRoot">The root path of the workspace.</param>
    /// <returns>The full path to the newly created run directory.</returns>
    public string CreateRunDirectory(string workspaceRoot)
    {
        string root = Path.Combine(workspaceRoot, ".agent-harness", "runs");
        string runId = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfff");
        string runDir = Path.Combine(root, runId);
        Directory.CreateDirectory(runDir);
        return runDir;
    }

    /// <summary>
    /// Serializes a payload to a redacted JSON run log file in the specified run directory.
    /// </summary>
    /// <param name="runDirectory">The directory where the run log file will be written.</param>
    /// <param name="payload">The object to serialize as the run log.</param>
    /// <param name="cancellationToken">Token to signal cancellation.</param>
    /// <returns>A task representing the asynchronous write operation.</returns>
    public Task WriteRunLogAsync(string runDirectory, object payload, CancellationToken cancellationToken)
    {
        string serialized = JsonSerializer.Serialize(payload, JsonDefaults.Indented);
        string redacted = Redaction.RedactSecrets(serialized);
        return File.WriteAllTextAsync(Path.Combine(runDirectory, "run-log.json"), redacted, cancellationToken);
    }
}
