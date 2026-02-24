using ArchHarness.App.Core;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Copilot;

/// <summary>
/// Manages the lifecycle of the underlying GitHub Copilot SDK client.
/// </summary>
public sealed class CopilotClientProvider : IAsyncDisposable
{
    private readonly Task<GitHub.Copilot.SDK.CopilotClient> _clientTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="CopilotClientProvider"/> class
    /// and starts the SDK client asynchronously.
    /// </summary>
    /// <param name="options">Copilot configuration options.</param>
    public CopilotClientProvider(IOptions<CopilotOptions> options)
    {
        _clientTask = InitializeClientAsync(options.Value);
    }

    /// <summary>
    /// Returns the initialized SDK client, awaiting startup if still in progress.
    /// </summary>
    /// <returns>The fully initialized SDK client.</returns>
    public Task<GitHub.Copilot.SDK.CopilotClient> GetClientAsync() => _clientTask;

    /// <summary>
    /// Disposes the underlying SDK client if it was successfully started.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_clientTask.IsCompletedSuccessfully)
        {
            await _clientTask.Result.DisposeAsync();
        }
    }

    private static async Task<GitHub.Copilot.SDK.CopilotClient> InitializeClientAsync(CopilotOptions options)
    {
        var clientOptions = CopilotClientOptionsFactory.Build(options, autoRestart: true);
        var client = new GitHub.Copilot.SDK.CopilotClient(clientOptions);
        await client.StartAsync();
        return client;
    }
}
