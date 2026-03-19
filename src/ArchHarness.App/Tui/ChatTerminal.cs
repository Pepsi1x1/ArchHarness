using ArchHarness.App.Core;
using ArchHarness.App.Copilot;

namespace ArchHarness.App.Tui;

/// <summary>
/// Thin coordinator that owns the top-level terminal flow, delegating screen routing,
/// rendering, and run monitoring to focused collaborators.
/// </summary>
public sealed class ChatTerminal
    : IApplicationHost
{
    private readonly OrchestratorRuntime _runtime;
    private readonly ConversationController _conversationController;
    private readonly IStartupPreflightValidator _preflightValidator;
    private readonly IUserInputState _userInputState;
    private readonly IAgentStreamEventStream _agentStreamEventStream;

    private static readonly Dictionary<TuiScreen, Action<RunRequest, string, RunArtefacts, List<RuntimeProgressEvent>>> SCREEN_RENDERERS =
        new Dictionary<TuiScreen, Action<RunRequest, string, RunArtefacts, List<RuntimeProgressEvent>>>
        {
            [TuiScreen.ChatSetup] = (request, setupSummary, artefacts, runEvents) =>
                SetupScreenRenderer.RenderSetupScreen(request, setupSummary),
            [TuiScreen.RunMonitor] = (request, setupSummary, artefacts, runEvents) =>
                RunMonitor.RenderComplete(artefacts, runEvents),
            [TuiScreen.Logs] = (request, setupSummary, artefacts, runEvents) =>
                ContentScreenRenderer.RenderFileScreen("Logs", Path.Combine(artefacts.RunDirectory, "events.jsonl"), 80),
            [TuiScreen.Artefacts] = (request, setupSummary, artefacts, runEvents) =>
                ContentScreenRenderer.RenderArtefactsScreen(artefacts.RunDirectory),
            [TuiScreen.Review] = (request, setupSummary, artefacts, runEvents) =>
                ContentScreenRenderer.RenderFileScreen("Review Viewer", Path.Combine(artefacts.RunDirectory, "ArchitectureReview.json"), 120),
            [TuiScreen.Prompts] = (request, setupSummary, artefacts, runEvents) =>
                ContentScreenRenderer.RenderPromptsScreen(runEvents)
        };

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatTerminal"/> class.
    /// </summary>
    /// <param name="runtime">The orchestrator runtime that executes runs.</param>
    /// <param name="conversationController">Builds run requests from user input.</param>
    /// <param name="preflightValidator">Validates startup prerequisites.</param>
    /// <param name="userInputState">Tracks whether the agent is awaiting user input.</param>
    /// <param name="agentStreamEventStream">Streams real-time agent delta content events.</param>
    public ChatTerminal(
        OrchestratorRuntime runtime,
        ConversationController conversationController,
        IStartupPreflightValidator preflightValidator,
        IUserInputState userInputState,
        IAgentStreamEventStream agentStreamEventStream)
    {
        this._runtime = runtime;
        this._conversationController = conversationController;
        this._preflightValidator = preflightValidator;
        this._userInputState = userInputState;
        this._agentStreamEventStream = agentStreamEventStream;
    }

    /// <summary>
    /// Runs the full terminal UI lifecycle: splash, preflight, setup, monitoring, and post-run navigation.
    /// </summary>
    /// <param name="args">Command-line arguments forwarded to the conversation controller.</param>
    /// <param name="cancellationToken">Token to signal cancellation.</param>
    public async Task RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        ContentScreenRenderer.RenderSplash();

        if (!await this.ValidatePreflightAsync(cancellationToken))
        {
            return;
        }

        BuildRunRequestResult? requestResult = await this.TryBuildRunRequestAsync(args, cancellationToken);
        if (requestResult is null)
        {
            return;
        }

        await WaitForSetupAcknowledgementAsync(requestResult.Request, requestResult.SetupSummary);

        List<RuntimeProgressEvent> runEvents = new List<RuntimeProgressEvent>();
        Progress<RuntimeProgressEvent> progress = CreateRunProgress(runEvents);

        AgentStreamState agentStreamState = new AgentStreamState(this._agentStreamEventStream);
        using CancellationTokenSource runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using CancellationTokenSource agentStreamCts = CancellationTokenSource.CreateLinkedTokenSource(runCts.Token);
        Task agentStreamTask = agentStreamState.ConsumeAsync(agentStreamCts.Token);
        Task<RunArtefacts> runTask = this._runtime.RunAsync(requestResult.Request, progress, cancellationToken: runCts.Token);

        bool userCanceledRun = await this.MonitorRunAsync(runTask, runEvents, agentStreamState, runCts);
        await StopAgentStreamAsync(agentStreamCts, agentStreamTask);

        RunArtefacts? artefacts = await TryAwaitRunArtefactsAsync(runTask, userCanceledRun, cancellationToken);
        if (artefacts is null)
        {
            return;
        }

        await ScreenLoopAsync(requestResult.Request, requestResult.SetupSummary, artefacts, runEvents, cancellationToken);
    }

    private async Task<bool> ValidatePreflightAsync(CancellationToken cancellationToken)
    {
        PreflightValidationResult preflight = await this._preflightValidator.ValidateAsync(cancellationToken);
        if (preflight.IsSuccess)
        {
            return true;
        }

        RunResultRenderer.RenderPreflightFailure(preflight);
        return false;
    }

    private async Task<BuildRunRequestResult?> TryBuildRunRequestAsync(string[] args, CancellationToken cancellationToken)
    {
        try
        {
            (RunRequest request, string setupSummary) = await this._conversationController.BuildRunRequestAsync(args, cancellationToken);
            return new BuildRunRequestResult(request, setupSummary);
        }
        catch (OperationCanceledException)
        {
            RunResultRenderer.RenderExitMessage();
            return null;
        }
        catch (InvalidOperationException ex)
        {
            RunResultRenderer.RenderRunFailure(ex);
            return null;
        }
    }

    private static async Task WaitForSetupAcknowledgementAsync(RunRequest request, string setupSummary)
    {
        SetupScreenRenderer.RenderSetupScreen(request, setupSummary);
        Console.CursorVisible = true;
        if (!Console.IsInputRedirected)
        {
            _ = TryReadKey(out _);
        }

        Console.CursorVisible = false;
        await Task.CompletedTask;
    }

    private static Progress<RuntimeProgressEvent> CreateRunProgress(List<RuntimeProgressEvent> runEvents)
        => new Progress<RuntimeProgressEvent>(evt =>
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
                    liveScreenInitialized);
                if (isAwaitingInput)
                {
                    continue;
                }

                if (ProcessLiveRunKeys(agentStreamState, runCts, ref userCanceledRun))
                {
                    break;
                }

                RenderLiveRun(runEvents, agentStreamState, spinner[spinnerIndex], ref liveScreenInitialized);
                spinnerIndex = (spinnerIndex + 1) % spinner.Length;
                await Task.Delay(160, runCts.Token);
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

        await Task.Delay(140, cancellationToken);
        return (true, awaitingInputBannerShown, liveScreenInitialized);
    }

    private static bool ProcessLiveRunKeys(AgentStreamState agentStreamState, CancellationTokenSource runCts, ref bool userCanceledRun)
    {
        while (!Console.IsInputRedirected && Console.KeyAvailable)
        {
            if (!TryReadKey(out ConsoleKeyInfo keyInfo))
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
            await agentStreamCts.CancelAsync();
        }

        try
        {
            await agentStreamTask;
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
            return await runTask;
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

    private static async Task ScreenLoopAsync(
        RunRequest request,
        string setupSummary,
        RunArtefacts artefacts,
        List<RuntimeProgressEvent> runEvents,
        CancellationToken cancellationToken)
    {
        TuiScreen screen = TuiScreen.RunMonitor;
        while (!cancellationToken.IsCancellationRequested)
        {
            RenderScreen(screen, request, setupSummary, artefacts, runEvents);

            FooterRenderer.RenderFooter();
            Console.CursorVisible = true;
            if (Console.IsInputRedirected)
            {
                RunResultRenderer.RenderExitMessage();
                break;
            }

            if (!TryReadKey(out ConsoleKeyInfo keyInfo))
            {
                RunResultRenderer.RenderExitMessage();
                break;
            }

            ConsoleKey key = keyInfo.Key;
            Console.CursorVisible = false;
            if (ScreenRouter.IsQuitKey(key))
            {
                RunResultRenderer.RenderExitMessage();
                break;
            }

            screen = ScreenRouter.Navigate(key, screen);
            await Task.Yield();
        }
    }

    private static void RenderScreen(
        TuiScreen screen,
        RunRequest request,
        string setupSummary,
        RunArtefacts artefacts,
        List<RuntimeProgressEvent> runEvents)
    {
        if (SCREEN_RENDERERS.TryGetValue(screen, out Action<RunRequest, string, RunArtefacts, List<RuntimeProgressEvent>>? renderer))
        {
            renderer(request, setupSummary, artefacts, runEvents);
        }
    }

    private static bool TryReadKey(out ConsoleKeyInfo keyInfo)
    {
        keyInfo = default;
        try
        {
            keyInfo = Console.ReadKey(intercept: true);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private sealed record BuildRunRequestResult(RunRequest Request, string SetupSummary);
}
