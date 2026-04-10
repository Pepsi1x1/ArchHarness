using ArchHarness.App.Core;
using ArchHarness.App.Copilot;

namespace ArchHarness.App.Tui;

/// <summary>
/// Executes and monitors the live terminal run experience.
/// </summary>
public interface IChatTerminalRunController
{
    /// <summary>
    /// Runs the orchestrator and returns captured artifacts and progress when successful.
    /// </summary>
    Task<ChatTerminalRunResult?> ExecuteAsync(RunRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Captures the completed run artifacts and recorded runtime progress.
/// </summary>
public sealed record ChatTerminalRunResult(RunArtefacts Artefacts, List<RuntimeProgressEvent> RunEvents);

/// <summary>
/// Default implementation of <see cref="IChatTerminalRunController"/>.
/// </summary>
public sealed class ChatTerminalRunController : IChatTerminalRunController
{
    private readonly IOrchestratorRuntime _runtime;
    private readonly IUserInputState _userInputState;
    private readonly IAgentStreamEventStream _agentStreamEventStream;
    private readonly IConsoleInputReader _consoleInputReader;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatTerminalRunController"/> class.
    /// </summary>
    public ChatTerminalRunController(
        IOrchestratorRuntime runtime,
        IUserInputState userInputState,
        IAgentStreamEventStream agentStreamEventStream,
        IConsoleInputReader consoleInputReader)
    {
        this._runtime = runtime;
        this._userInputState = userInputState;
        this._agentStreamEventStream = agentStreamEventStream;
        this._consoleInputReader = consoleInputReader;
    }

    /// <inheritdoc />
    public async Task<ChatTerminalRunResult?> ExecuteAsync(RunRequest request, CancellationToken cancellationToken)
    {
        List<RuntimeProgressEvent> runEvents = new List<RuntimeProgressEvent>();
        Progress<RuntimeProgressEvent> progress = CreateRunProgress(runEvents);

        AgentStreamState agentStreamState = new(this._agentStreamEventStream);
        using CancellationTokenSource runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using CancellationTokenSource agentStreamCts = CancellationTokenSource.CreateLinkedTokenSource(runCts.Token);
        Task agentStreamTask = agentStreamState.ConsumeAsync(agentStreamCts.Token);
        Task<RunArtefacts> runTask = this._runtime.RunAsync(request, progress, cancellationToken: runCts.Token);

        bool userCanceledRun = await this.MonitorRunAsync(runTask, runEvents, agentStreamState, runCts).ConfigureAwait(false);
        await StopAgentStreamAsync(agentStreamCts, agentStreamTask).ConfigureAwait(false);

        RunArtefacts? artefacts = await TryAwaitRunArtefactsAsync(runTask, userCanceledRun, cancellationToken).ConfigureAwait(false);
        return artefacts is null ? null : new ChatTerminalRunResult(artefacts, runEvents);
    }

    private static Progress<RuntimeProgressEvent> CreateRunProgress(List<RuntimeProgressEvent> runEvents)
        => new(evt =>
        {
            lock (runEvents)
            {
                runEvents.Add(evt);
            }
        });

    private async Task<bool> MonitorRunAsync(
        Task<RunArtefacts> runTask,
        List<RuntimeProgressEvent> runEvents,
        AgentStreamState agentStreamState,
        CancellationTokenSource runCts)
    {
        char[] spinner = new[] { '|', '/', '-', '\\' };
        int spinnerIndex = 0;
        bool liveScreenInitialized = false;
        bool awaitingInputBannerShown = false;
        bool userCanceledRun = false;

        try
        {
            while (!runTask.IsCompleted)
            {
                (bool isAwaitingInput, awaitingInputBannerShown, liveScreenInitialized) = await this.HandleAwaitingInputAsync(
                    runCts.Token,
                    awaitingInputBannerShown,
                    liveScreenInitialized).ConfigureAwait(false);
                if (isAwaitingInput)
                {
                    continue;
                }

                if (this.ProcessLiveRunKeys(agentStreamState, runCts, ref userCanceledRun))
                {
                    break;
                }

                RenderLiveRun(runEvents, agentStreamState, spinner[spinnerIndex], ref liveScreenInitialized);
                spinnerIndex = (spinnerIndex + 1) % spinner.Length;
                await Task.Delay(160, runCts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (runCts.IsCancellationRequested)
        {
            // Expected when the user quits during the live run or the caller cancels the session.
        }

        return userCanceledRun;
    }

    private async Task<(bool IsAwaitingInput, bool AwaitingInputBannerShown, bool LiveScreenInitialized)> HandleAwaitingInputAsync(
        CancellationToken cancellationToken,
        bool awaitingInputBannerShown,
        bool liveScreenInitialized)
    {
        if (!this._userInputState.IsAwaitingInput)
        {
            if (awaitingInputBannerShown)
            {
                awaitingInputBannerShown = false;
            }

            return (false, awaitingInputBannerShown, liveScreenInitialized);
        }

        if (!awaitingInputBannerShown)
        {
            FooterRenderer.RenderAwaitingInputBanner(this._userInputState.ActiveQuestion);
            awaitingInputBannerShown = true;
            liveScreenInitialized = false;
        }

        await Task.Delay(140, cancellationToken).ConfigureAwait(false);
        return (true, awaitingInputBannerShown, liveScreenInitialized);
    }

    private bool ProcessLiveRunKeys(AgentStreamState agentStreamState, CancellationTokenSource runCts, ref bool userCanceledRun)
    {
        while (this._consoleInputReader.KeyAvailable)
        {
            if (!this._consoleInputReader.TryReadKey(out ConsoleKeyInfo keyInfo))
            {
                break;
            }

            if (ScreenRouter.IsQuitKey(keyInfo.Key))
            {
                userCanceledRun = true;
                runCts.Cancel();
                return true;
            }

            if (keyInfo.Key == ConsoleKey.A)
            {
                agentStreamState.CycleSelectedAgent();
            }
        }

        return userCanceledRun;
    }

    private static void RenderLiveRun(
        List<RuntimeProgressEvent> runEvents,
        AgentStreamState agentStreamState,
        char spinner,
        ref bool liveScreenInitialized)
    {
        IEnumerable<(string Id, string Role)> availableAgents = agentStreamState.GetAvailableAgents();
        RunMonitor.RenderLiveWithAgentView(
            runEvents,
            agentStreamState.Events,
            agentStreamState.SelectedAgentId,
            availableAgents,
            spinner,
            ref liveScreenInitialized);
    }

    private static async Task StopAgentStreamAsync(CancellationTokenSource agentStreamCts, Task agentStreamTask)
    {
        if (!agentStreamCts.IsCancellationRequested)
        {
            await agentStreamCts.CancelAsync().ConfigureAwait(false);
        }

        try
        {
            await agentStreamTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on run shutdown.
        }
    }

    private static async Task<RunArtefacts?> TryAwaitRunArtefactsAsync(Task<RunArtefacts> runTask, bool userCanceledRun, CancellationToken cancellationToken)
    {
        try
        {
            return await runTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (userCanceledRun || cancellationToken.IsCancellationRequested)
        {
            RunResultRenderer.RenderExitMessage();
            return null;
        }
        catch (Exception ex)
        {
            RunResultRenderer.RenderRunFailure(ex);
            return null;
        }
    }
}
