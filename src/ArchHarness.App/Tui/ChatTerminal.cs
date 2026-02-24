using ArchHarness.App.Core;
using ArchHarness.App.Copilot;

namespace ArchHarness.App.Tui;

/// <summary>
/// Thin coordinator that owns the top-level terminal flow, delegating screen routing,
/// rendering, and run monitoring to focused collaborators.
/// </summary>
public sealed class ChatTerminal
{
    private readonly OrchestratorRuntime _runtime;
    private readonly ConversationController _conversationController;
    private readonly IStartupPreflightValidator _preflightValidator;
    private readonly IUserInputState _userInputState;
    private readonly IAgentStreamEventStream _agentStreamEventStream;

    private static readonly Dictionary<TuiScreen, Action<RunRequest, string, RunArtefacts, List<RuntimeProgressEvent>>> _screenRenderers =
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

        PreflightValidationResult preflight = await this._preflightValidator.ValidateAsync(cancellationToken);
        if (!preflight.IsSuccess)
        {
            RunResultRenderer.RenderPreflightFailure(preflight);
            return;
        }

        (RunRequest request, string setupSummary) = await this._conversationController.BuildRunRequestAsync(args, cancellationToken);

        SetupScreenRenderer.RenderSetupScreen(request, setupSummary);
        Console.CursorVisible = true;
        Console.ReadKey(intercept: true);
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
        using CancellationTokenSource agentStreamCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task agentStreamTask = agentStreamState.ConsumeAsync(agentStreamCts.Token);

        Task<RunArtefacts> runTask = this._runtime.RunAsync(request, progress, cancellationToken);
        char[] spinner = new[] { '|', '/', '-', '\\' };
        int spinnerIndex = 0;
        bool liveScreenInitialized = false;

        while (!runTask.IsCompleted)
        {
            if (this._userInputState.IsAwaitingInput)
            {
                FooterRenderer.RenderAwaitingInputBanner(this._userInputState.ActiveQuestion);
                liveScreenInitialized = false;
                await Task.Delay(140, cancellationToken);
                continue;
            }

            while (Console.KeyAvailable)
            {
                ConsoleKeyInfo keyInfo = Console.ReadKey(intercept: true);
                if (keyInfo.Key == ConsoleKey.A)
                {
                    agentStreamState.CycleSelectedAgent();
                }
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
            await Task.Delay(160, cancellationToken);
        }

        await agentStreamCts.CancelAsync();
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
            ConsoleKey key = Console.ReadKey(intercept: true).Key;
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
        if (_screenRenderers.TryGetValue(screen, out Action<RunRequest, string, RunArtefacts, List<RuntimeProgressEvent>>? renderer))
        {
            renderer(request, setupSummary, artefacts, runEvents);
        }
    }
}
