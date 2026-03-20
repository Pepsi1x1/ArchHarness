using ArchHarness.App.Storage;

namespace ArchHarness.App.Core;

/// <summary>
/// Abstracts the orchestrator runtime so that host layers (Web, TUI) depend on a contract
/// rather than the concrete <see cref="OrchestratorRuntime"/> implementation.
/// </summary>
public interface IOrchestratorRuntime
{
    /// <summary>
    /// Executes a full orchestrated run: workspace initialization, plan execution, architecture review,
    /// completion validation, and artifact persistence.
    /// </summary>
    Task<RunArtefacts> RunAsync(
        RunRequest request,
        IProgress<RuntimeProgressEvent>? progress = null,
        Action<string, string>? onRunContextEstablished = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes an interrupted run from its persisted checkpoint.
    /// </summary>
    Task<RunArtefacts> ResumeAsync(
        PersistedRunState runState,
        IProgress<RuntimeProgressEvent>? progress = null,
        Action<string, string>? onRunContextEstablished = null,
        CancellationToken cancellationToken = default);
}
