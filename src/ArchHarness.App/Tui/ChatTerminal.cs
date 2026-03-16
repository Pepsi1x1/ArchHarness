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
        Task<PreflightValidationResult> preflightTask = this._preflightValidator.ValidateAsync(cancellationToken);
        ContentScreenRenderer.RenderSplash();

        PreflightValidationResult preflight = await preflightTask;
        if (!preflight.IsSuccess)
        {
            RunResultRenderer.RenderPreflightFailure(preflight);
            return;
        }

        RunRequest request;
        string setupSummary;
        try
        {
            (request, setupSummary) = await this._conversationController.BuildRunRequestAsync(args, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            RunResultRenderer.RenderExitMessage();
            return;
        }
        catch (InvalidOperationException ex)
        {
            RunResultRenderer.RenderRunFailure(ex);
            return;
        }

        SetupScreenRenderer.RenderSetupScreen(request, setupSummary);
        Console.CursorVisible = true;
        if (!Console.IsInputRedirected)
        {
            _ = TryReadKey(out _);
        }
        Console.CursorVisible = false;

        List<RuntimeProgressEvent> runEvents = new List<RuntimeProgressEvent>();
        Progress<RuntimeProgressEvent> progress = new Progress<RuntimeProgressEvent>(evt =>
        {
            lock (runEvents)
            {
                runEvents.Add(evt);
            }
        });

        AgentStreamState agentStreamState = new AgentStreamState(this._agentStreamEventStream);
        using CancellationTokenSource runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using CancellationTokenSource agentStreamCts = CancellationTokenSource.CreateLinkedTokenSource(runCts.Token);
        Task agentStreamTask = agentStreamState.ConsumeAsync(agentStreamCts.Token);

        Task<RunArtefacts> runTask = this._runtime.RunAsync(request, progress, cancellationToken: runCts.Token);
        char[] spinner = new[] { '|', '/', '-', '\\' };
        int spinnerIndex = 0;
        bool liveScreenInitialized = false;
        bool awaitingInputBannerShown = false;
        bool userCanceledRun = false;

        try
        {
            while (!runTask.IsCompleted)
            {
                if (this._userInputState.IsAwaitingInput)
                {
                    if (!awaitingInputBannerShown)
                    {
                        FooterRenderer.RenderAwaitingInputBanner(this._userInputState.ActiveQuestion);
                        awaitingInputBannerShown = true;
                        liveScreenInitialized = false;
                    }

                    await Task.Delay(140, runCts.Token);
                    continue;
                }

                if (awaitingInputBannerShown)
                {
                    awaitingInputBannerShown = false;
                }

                while (!Console.IsInputRedirected && Console.KeyAvailable)
                {
                    if (!TryReadKey(out ConsoleKeyInfo keyInfo))
                    {
                        break;
                    }

                    if (ScreenRouter.IsQuitKey(keyInfo.Key))
                    {
                        userCanceledRun = true;
                        await runCts.CancelAsync();
                        break;
                    }

                    if (keyInfo.Key == ConsoleKey.A)
                    {
                        agentStreamState.CycleSelectedAgent();
                    }
                }

                if (userCanceledRun)
                {
                    break;
                }

                IEnumerable<(string Id, string Role)> availableAgents = agentStreamState.GetAvailableAgents();

                RunMonitor.RenderLiveWithAgentView(
                    runEvents,
                    agentStreamState.Events,
                    agentStreamState.SelectedAgentId,
                    availableAgents,
                    spinner[spinnerIndex],
                    ref liveScreenInitialized);

                spinnerIndex = (spinnerIndex + 1) % spinner.Length;
                await Task.Delay(160, runCts.Token);
            }
        }
        catch (OperationCanceledException) when (runCts.IsCancellationRequested)
        {
            // Expected when the user quits during the live run or the caller cancels the session.
        }

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

        RunArtefacts artefacts;
        try
        {
            artefacts = await runTask;
        }
        catch (OperationCanceledException) when (userCanceledRun || cancellationToken.IsCancellationRequested)
        {
            RunResultRenderer.RenderExitMessage();
            return;
        }
        catch (Exception ex)
        {
            RunResultRenderer.RenderRunFailure(ex);
            return;
        }

        await ScreenLoopAsync(request, setupSummary, artefacts, runEvents, cancellationToken);
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
}
