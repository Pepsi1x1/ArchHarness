using System.Collections.Concurrent;
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
/// Manages the lifecycle of the underlying GitHub Copilot SDK client.
/// </summary>
public sealed class CopilotClientProvider : ICopilotClientProvider, IAsyncDisposable
{
    private readonly CopilotOptions _options;
    private readonly IWorkspaceRootAccessor _workspaceRootAccessor;
    private readonly ConcurrentDictionary<string, Lazy<Task<GitHub.Copilot.SDK.CopilotClient>>> _clients
        = new ConcurrentDictionary<string, Lazy<Task<GitHub.Copilot.SDK.CopilotClient>>>(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="CopilotClientProvider"/> class
    /// and defers SDK client startup until first use.
    /// </summary>
    /// <param name="options">Copilot configuration options.</param>
    /// <param name="workspaceRootAccessor">Accessor for the active workspace root.</param>
    public CopilotClientProvider(IOptions<CopilotOptions> options, IWorkspaceRootAccessor workspaceRootAccessor)
    {
        this._options = options.Value;
        this._workspaceRootAccessor = workspaceRootAccessor;
    }

    /// <summary>
    /// Returns the initialized SDK client for the current async-local workspace root,
    /// spawning a new CLI process only when no client has yet been created for that directory.
    /// Separate working directories keep separate clients so that parallel work against
    /// different workspaces does not dispose each other's sessions.
    /// </summary>
    /// <returns>The fully initialized SDK client.</returns>
    public Task<GitHub.Copilot.SDK.CopilotClient> GetClientAsync()
    {
        string desiredWorkingDirectory = ResolveWorkingDirectory(this._workspaceRootAccessor.Current);
        Lazy<Task<GitHub.Copilot.SDK.CopilotClient>> lazy = this._clients.GetOrAdd(
            desiredWorkingDirectory,
            cwd => new Lazy<Task<GitHub.Copilot.SDK.CopilotClient>>(
                () => InitializeClientAsync(this._options, cwd),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return this.AwaitClientAsync(desiredWorkingDirectory, lazy);
    }

    private async Task<GitHub.Copilot.SDK.CopilotClient> AwaitClientAsync(
        string workingDirectory,
        Lazy<Task<GitHub.Copilot.SDK.CopilotClient>> lazy)
    {
        try
        {
            return await lazy.Value.ConfigureAwait(false);
        }
        catch
        {
            // Startup failures should not poison the cache; allow the next caller to retry.
            this._clients.TryRemove(new KeyValuePair<string, Lazy<Task<GitHub.Copilot.SDK.CopilotClient>>>(workingDirectory, lazy));
            throw;
        }
    }

    /// <inheritdoc />
    public async Task InvalidateAsync()
    {
        string desiredWorkingDirectory = ResolveWorkingDirectory(this._workspaceRootAccessor.Current);
        if (!this._clients.TryRemove(desiredWorkingDirectory, out Lazy<Task<GitHub.Copilot.SDK.CopilotClient>>? removed))
        {
            return;
        }

        if (removed.IsValueCreated && removed.Value.IsCompletedSuccessfully)
        {
            try { await removed.Value.Result.DisposeAsync().ConfigureAwait(false); } catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// Disposes all cached SDK clients.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        foreach (Lazy<Task<GitHub.Copilot.SDK.CopilotClient>> lazy in this._clients.Values)
        {
            if (lazy.IsValueCreated && lazy.Value.IsCompletedSuccessfully)
            {
                try { await lazy.Value.Result.DisposeAsync().ConfigureAwait(false); } catch { /* best-effort */ }
            }
        }

        this._clients.Clear();
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
