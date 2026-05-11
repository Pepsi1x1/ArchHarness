using ArchHarness.App.Copilot;
using ArchHarness.App.Core;

namespace ArchHarness.App.Tui;

/// <summary>
/// Thin coordinator that owns the top-level terminal flow, delegating screen routing,
/// rendering, and run monitoring to focused collaborators.
/// </summary>
public sealed class ChatTerminal
    : IApplicationHost
{
    private readonly ConversationController _conversationController;
    private readonly IChatTerminalRunController _runController;
    private readonly IChatTerminalScreenNavigator _screenNavigator;
    private readonly IStartupPreflightValidator _preflightValidator;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatTerminal"/> class.
    /// </summary>
    /// <param name="conversationController">Builds run requests from user input.</param>
    /// <param name="runController">Executes and monitors live runs.</param>
    /// <param name="screenNavigator">Navigates post-run screens.</param>
    /// <param name="preflightValidator">Validates startup prerequisites.</param>
    public ChatTerminal(
        ConversationController conversationController,
        IChatTerminalRunController runController,
        IChatTerminalScreenNavigator screenNavigator,
        IStartupPreflightValidator preflightValidator)
    {
        this._conversationController = conversationController;
        this._runController = runController;
        this._screenNavigator = screenNavigator;
        this._preflightValidator = preflightValidator;
    }

    /// <summary>
    /// Runs the full terminal UI lifecycle: splash, preflight, setup, monitoring, and post-run navigation.
    /// </summary>
    /// <param name="args">Command-line arguments forwarded to the conversation controller.</param>
    /// <param name="cancellationToken">Token to signal cancellation.</param>
    public async Task RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        bool nonInteractiveCommand = CliArgumentParser.IsNonInteractiveCommand(args);
        if (!nonInteractiveCommand)
        {
            ContentScreenRenderer.RenderSplash();
        }

        if (!await this.ValidatePreflightAsync(cancellationToken))
        {
            return;
        }

        BuildRunRequestResult? requestResult = await this.TryBuildRunRequestAsync(args, cancellationToken);
        if (requestResult is null)
        {
            return;
        }

        if (!nonInteractiveCommand)
        {
            await WaitForSetupAcknowledgementAsync(requestResult.Request, requestResult.SetupSummary);
        }

        ChatTerminalRunResult? result = await this._runController.ExecuteAsync(requestResult.Request, !nonInteractiveCommand, cancellationToken);
        if (result is null)
        {
            return;
        }

        if (nonInteractiveCommand)
        {
            RunResultRenderer.RenderCommandCompletion(result.Artefacts);
            return;
        }

        await this._screenNavigator.ShowAsync(
            requestResult.Request,
            requestResult.SetupSummary,
            result.Artefacts,
            result.RunEvents,
            cancellationToken);
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
            try
            {
                _ = Console.ReadKey(intercept: true);
            }
            catch (IOException)
            {
                // Console stream closed — no acknowledgement required.
            }
            catch (InvalidOperationException)
            {
                // Console unavailable — no acknowledgement required.
            }
        }

        Console.CursorVisible = false;
        await Task.CompletedTask;
    }

    private sealed record BuildRunRequestResult(RunRequest Request, string SetupSummary);
}
