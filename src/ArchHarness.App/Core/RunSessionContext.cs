using ArchHarness.App.Copilot;
using ArchHarness.App.Storage;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Core;

/// <summary>
/// Groups the session-scoped services needed by <see cref="OrchestratorRuntime"/>:
/// agent configuration options, the Copilot client for usage snapshots, and
/// run-state persistence. Extracted as a facade to keep the runtime constructor
/// within the five-dependency limit.
/// </summary>
public sealed class RunSessionContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RunSessionContext"/> class.
    /// </summary>
    /// <param name="agentsOptions">Resolved agent configuration options.</param>
    /// <param name="copilotClient">The Copilot client used for usage reporting.</param>
    /// <param name="runStateStore">Persists and retrieves resumable run-state checkpoints.</param>
    public RunSessionContext(
        IOptions<AgentsOptions> agentsOptions,
        ICopilotClient copilotClient,
        IRunStateStore runStateStore)
    {
        this.AgentsOptions = agentsOptions.Value;
        this.CopilotClient = copilotClient;
        this.RunStateStore = runStateStore;
    }

    /// <summary>
    /// Gets the resolved agent configuration options.
    /// </summary>
    public AgentsOptions AgentsOptions { get; }

    /// <summary>
    /// Gets the Copilot client used for per-run usage snapshots.
    /// </summary>
    public ICopilotClient CopilotClient { get; }

    /// <summary>
    /// Gets the store used to persist and retrieve resumable run-state checkpoints.
    /// </summary>
    public IRunStateStore RunStateStore { get; }
}
