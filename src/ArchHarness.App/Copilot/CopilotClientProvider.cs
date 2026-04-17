using ArchHarness.App.Core;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Copilot;

/// <summary>
/// Defines the contract for providing the initialized Copilot SDK client.
/// </summary>
public interface ICopilotClientProvider
{
    /// <summary>
    /// Returns the initialized SDK client, awaiting startup if still in progress.
    /// </summary>
    Task<GitHub.Copilot.SDK.CopilotClient> GetClientAsync();

    /// <summary>
    /// Forces the current SDK client to be disposed so that the next call to
    /// <see cref="GetClientAsync"/> spawns a fresh CLI process.
    /// </summary>
    Task InvalidateAsync();
}

/// <summary>
/// Manages the lifecycle of a single shared GitHub Copilot SDK client.
/// The client process is started once with an initial cwd; per-session cwd is carried
/// on <c>SessionConfig.WorkingDirectory</c> / <c>ResumeSessionConfig.WorkingDirectory</c>
/// so parallel sessions against different workspace roots do not require separate clients
/// and never cause the shared client to be disposed mid-turn.
/// </summary>
public sealed class CopilotClientProvider : ICopilotClientProvider, IAsyncDisposable
{
    private readonly CopilotOptions _options;
    private readonly IWorkspaceRootAccessor _workspaceRootAccessor;
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
    private Task<GitHub.Copilot.SDK.CopilotClient>? _clientTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="CopilotClientProvider"/> class
    /// and defers SDK client startup until first use.
    /// </summary>
    /// <param name="options">Copilot configuration options.</param>
    /// <param name="workspaceRootAccessor">Accessor for the active workspace root, used only to pick the CLI's initial cwd on first start.</param>
    public CopilotClientProvider(IOptions<CopilotOptions> options, IWorkspaceRootAccessor workspaceRootAccessor)
    {
        this._options = options.Value;
        this._workspaceRootAccessor = workspaceRootAccessor;
    }

    /// <summary>
    /// Returns the initialized SDK client, awaiting startup if still in progress.
    /// The CLI process is started exactly once per provider lifetime (or after an
    /// explicit <see cref="InvalidateAsync"/>) and is reused across all workspace roots.
    /// </summary>
    /// <returns>The fully initialized SDK client.</returns>
    public async Task<GitHub.Copilot.SDK.CopilotClient> GetClientAsync()
    {
        if (this._clientTask is { } existing)
        {
            return await existing.ConfigureAwait(false);
        }

        await this._gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (this._clientTask is null)
            {
                string initialCwd = ResolveWorkingDirectory(this._workspaceRootAccessor.Current);
                this._clientTask = InitializeClientAsync(this._options, initialCwd);
            }

            return await this._clientTask.ConfigureAwait(false);
        }
        finally
        {
            this._gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task InvalidateAsync()
    {
        await this._gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (this._clientTask is not null && this._clientTask.IsCompletedSuccessfully)
            {
                try { await this._clientTask.Result.DisposeAsync().ConfigureAwait(false); } catch { /* best-effort */ }
            }

            this._clientTask = null;
        }
        finally
        {
            this._gate.Release();
        }
    }

    /// <summary>
    /// Disposes the underlying SDK client if it was successfully started.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (this._clientTask is not null && this._clientTask.IsCompletedSuccessfully)
        {
            await this._clientTask.Result.DisposeAsync();
        }

        this._gate.Dispose();
    }

    private static async Task<GitHub.Copilot.SDK.CopilotClient> InitializeClientAsync(CopilotOptions options, string workingDirectory)
    {
        GitHub.Copilot.SDK.CopilotClientOptions clientOptions = CopilotClientOptionsFactory.Build(options, autoRestart: true, workingDirectory);
        GitHub.Copilot.SDK.CopilotClient client = new GitHub.Copilot.SDK.CopilotClient(clientOptions);
        await client.StartAsync();
        return client;
    }

    private static string ResolveWorkingDirectory(string? workspaceRoot)
    {
        if (!string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(workspaceRoot));
        }

        return Directory.GetCurrentDirectory();
    }
}
