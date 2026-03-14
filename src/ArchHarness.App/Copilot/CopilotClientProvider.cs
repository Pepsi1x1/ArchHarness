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
}

/// <summary>
/// Manages the lifecycle of the underlying GitHub Copilot SDK client.
/// </summary>
public sealed class CopilotClientProvider : ICopilotClientProvider, IAsyncDisposable
{
    private readonly Task<GitHub.Copilot.SDK.CopilotClient> _clientTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="CopilotClientProvider"/> class
    /// and starts the SDK client asynchronously.
    /// </summary>
    /// <param name="options">Copilot configuration options.</param>
    public CopilotClientProvider(IOptions<CopilotOptions> options)
    {
        this._clientTask = InitializeClientAsync(options.Value);
    }

    /// <summary>
    /// Returns the initialized SDK client, awaiting startup if still in progress.
    /// </summary>
    /// <returns>The fully initialized SDK client.</returns>
    public Task<GitHub.Copilot.SDK.CopilotClient> GetClientAsync() => this._clientTask;

    /// <summary>
    /// Disposes the underlying SDK client if it was successfully started.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (this._clientTask.IsCompletedSuccessfully)
        {
            await this._clientTask.Result.DisposeAsync();
        }
    }

    private static async Task<GitHub.Copilot.SDK.CopilotClient> InitializeClientAsync(CopilotOptions options)
    {
        GitHub.Copilot.SDK.CopilotClientOptions clientOptions = CopilotClientOptionsFactory.Build(options, autoRestart: true);
        GitHub.Copilot.SDK.CopilotClient client = new GitHub.Copilot.SDK.CopilotClient(clientOptions);
        await client.StartAsync();
        return client;
    }
}
