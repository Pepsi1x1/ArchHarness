using ArchHarness.App.Core;

namespace ArchHarness.App.Tui;

/// <summary>
/// Handles post-run screen navigation inside the terminal UI.
/// </summary>
public interface IChatTerminalScreenNavigator
{
    /// <summary>
    /// Displays the post-run screen loop until the user exits.
    /// </summary>
    Task ShowAsync(
        RunRequest request,
        string setupSummary,
        RunArtefacts artefacts,
        List<RuntimeProgressEvent> runEvents,
        CancellationToken cancellationToken);
}

/// <summary>
/// Default implementation of <see cref="IChatTerminalScreenNavigator"/>.
/// </summary>
public sealed class ChatTerminalScreenNavigator : IChatTerminalScreenNavigator
{
    private readonly IConsoleInputReader _consoleInputReader;

    private static readonly Dictionary<TuiScreen, Action<RunRequest, string, RunArtefacts, List<RuntimeProgressEvent>>> _screenRenderers =
        new()
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
    /// Initializes a new instance of the <see cref="ChatTerminalScreenNavigator"/> class.
    /// </summary>
    public ChatTerminalScreenNavigator(IConsoleInputReader consoleInputReader)
    {
        this._consoleInputReader = consoleInputReader;
    }

    /// <inheritdoc />
    public async Task ShowAsync(
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
            if (this._consoleInputReader.IsInputRedirected)
            {
                RunResultRenderer.RenderExitMessage();
                break;
            }

            if (!this._consoleInputReader.TryReadKey(out ConsoleKeyInfo keyInfo))
            {
                RunResultRenderer.RenderExitMessage();
                break;
            }

            Console.CursorVisible = false;
            if (ScreenRouter.IsQuitKey(keyInfo.Key))
            {
                RunResultRenderer.RenderExitMessage();
                break;
            }

            screen = ScreenRouter.Navigate(keyInfo.Key, screen);
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
