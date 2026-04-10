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
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
    private Task<GitHub.Copilot.SDK.CopilotClient>? _clientTask;
    private string? _clientWorkingDirectory;

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
    /// Returns the initialized SDK client, awaiting startup if still in progress.
    /// </summary>
    /// <returns>The fully initialized SDK client.</returns>
    public async Task<GitHub.Copilot.SDK.CopilotClient> GetClientAsync()
    {
        string desiredWorkingDirectory = ResolveWorkingDirectory(this._workspaceRootAccessor.Current);

        await this._gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (this._clientTask is null || !string.Equals(this._clientWorkingDirectory, desiredWorkingDirectory, StringComparison.Ordinal))
            {
                if (this._clientTask is not null && this._clientTask.IsCompletedSuccessfully)
                {
                    await this._clientTask.Result.DisposeAsync().ConfigureAwait(false);
                }

                this._clientWorkingDirectory = desiredWorkingDirectory;
                this._clientTask = InitializeClientAsync(this._options, desiredWorkingDirectory);
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
            this._clientWorkingDirectory = null;
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
