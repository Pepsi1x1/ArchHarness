using ArchHarness.App.Core;
using ArchHarness.App.Copilot;

namespace ArchHarness.App.Tui;

public sealed class ChatTerminal
{
    private readonly OrchestratorRuntime _runtime;
    private readonly ConversationController _conversationController;
    private readonly IStartupPreflightValidator _preflightValidator;
    private readonly IUserInputState _userInputState;

    public ChatTerminal(
        OrchestratorRuntime runtime,
        ConversationController conversationController,
        IStartupPreflightValidator preflightValidator,
        IUserInputState userInputState)
    {
        _runtime = runtime;
        _conversationController = conversationController;
        _preflightValidator = preflightValidator;
        _userInputState = userInputState;
    }

    public async Task RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var preflight = await _preflightValidator.ValidateAsync(cancellationToken);
        if (!preflight.IsSuccess)
        {
            RenderPreflightFailure(preflight);
            return;
        }

        var (request, setupSummary) = await _conversationController.BuildRunRequestAsync(args, cancellationToken);

        RenderSetupScreen(request, setupSummary);
        Console.ReadKey(intercept: true);

        var runEvents = new List<RuntimeProgressEvent>();
        var progress = new Progress<RuntimeProgressEvent>(evt =>
        {
            lock (runEvents)
            {
                runEvents.Add(evt);
            }
        });

        var runTask = _runtime.RunAsync(request, progress, cancellationToken);
        var spinner = new[] { '|', '/', '-', '\\' };
        var spinnerIndex = 0;
        var liveScreenInitialized = false;

        while (!runTask.IsCompleted)
        {
            if (_userInputState.IsAwaitingInput)
            {
                RenderAwaitingInputBanner(_userInputState.ActiveQuestion);
                liveScreenInitialized = false;
                await Task.Delay(140, cancellationToken);
                continue;
            }

            RenderRunMonitorLive(runEvents, spinner[spinnerIndex], ref liveScreenInitialized);
            spinnerIndex = (spinnerIndex + 1) % spinner.Length;
            await Task.Delay(160, cancellationToken);
        }

        RunArtefacts artefacts;
        try
        {
            artefacts = await runTask;
        }
        catch (Exception ex)
        {
            RenderRunFailure(ex);
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
        var screen = Screen.RunMonitor;
        while (!cancellationToken.IsCancellationRequested)
        {
            switch (screen)
            {
                case Screen.ChatSetup:
                    RenderSetupScreen(request, setupSummary);
                    break;
                case Screen.RunMonitor:
                    RenderRunMonitorComplete(artefacts, runEvents);
                    break;
                case Screen.Logs:
                    RenderFileScreen("Logs", Path.Combine(artefacts.RunDirectory, "events.jsonl"), 80);
                    break;
                case Screen.Artefacts:
                    RenderArtefactsScreen(artefacts.RunDirectory);
                    break;
                case Screen.Review:
                    RenderFileScreen("Review Viewer", Path.Combine(artefacts.RunDirectory, "ArchitectureReview.json"), 120);
                    break;
                case Screen.Prompts:
                    RenderPromptsScreen(runEvents);
                    break;
            }

            RenderFooter();
            var key = Console.ReadKey(intercept: true).Key;
            if (key is ConsoleKey.Q or ConsoleKey.Escape)
            {
                break;
            }

            screen = key switch
            {
                ConsoleKey.D1 => Screen.ChatSetup,
                ConsoleKey.D2 => Screen.RunMonitor,
                ConsoleKey.D3 => Screen.Logs,
                ConsoleKey.D4 => Screen.Artefacts,
                ConsoleKey.D5 => Screen.Review,
                ConsoleKey.D6 => Screen.Prompts,
                _ => screen
            };

            await Task.Yield();
        }
    }

    private static void RenderSetupScreen(RunRequest request, string setupSummary)
    {
        Console.Clear();
        Console.WriteLine("=== Chat / Setup ===");
        Console.WriteLine($"Task: {request.TaskPrompt}");
        Console.WriteLine($"Workspace: {request.WorkspacePath}");
        Console.WriteLine($"Mode: {request.WorkspaceMode}");
        Console.WriteLine("Workflow: auto (orchestrator-driven)");
        Console.WriteLine($"Build: {request.BuildCommand ?? "(none)"}");
        Console.WriteLine();
        Console.WriteLine("Conversation Summary:");
        Console.WriteLine(setupSummary);
        Console.WriteLine();
        Console.WriteLine("Press any key to start run monitor...");
    }

    private static void RenderRunMonitorLive(List<RuntimeProgressEvent> events, char spinner, ref bool initialized)
    {
        const int maxEventRows = 16;
        var rows = new List<string>
        {
            "=== Run Monitor (Live) ===",
            $"Status: Running {spinner}",
            string.Empty,
            "Recent Agent States:"
        };

        List<RuntimeProgressEvent> snapshot;
        lock (events)
        {
            snapshot = events.TakeLast(maxEventRows).ToList();
        }

        foreach (var evt in snapshot)
        {
            rows.Add($"- [{evt.TimestampUtc:HH:mm:ss}] {evt.Source}: {evt.Message}");
            if (!string.IsNullOrWhiteSpace(evt.Prompt))
            {
                rows.Add($"  prompt: {SummarizePrompt(evt.Prompt)}");
            }
        }

        var targetRows = 4 + (maxEventRows * 2);
        while (rows.Count < targetRows)
        {
            rows.Add(string.Empty);
        }

        if (!initialized)
        {
            Console.Clear();
            initialized = true;
        }

        var width = Math.Max(20, Console.WindowWidth - 1);
        var maxRenderableRows = Math.Min(rows.Count, Math.Max(1, Console.WindowHeight - 1));
        for (var i = 0; i < maxRenderableRows; i++)
        {
            Console.SetCursorPosition(0, i);
            var text = rows[i];
            if (text.Length > width)
            {
                text = text[..width];
            }

            Console.Write(text.PadRight(width));
        }
    }

    private static void RenderRunMonitorComplete(RunArtefacts artefacts, List<RuntimeProgressEvent> events)
    {
        Console.Clear();
        Console.WriteLine("=== Run Monitor ===");
        Console.WriteLine("Status: Completed");
        Console.WriteLine($"RunId: {artefacts.RunId}");
        Console.WriteLine($"RunDirectory: {artefacts.RunDirectory}");
        Console.WriteLine();
        Console.WriteLine("Timeline:");

        List<RuntimeProgressEvent> snapshot;
        lock (events)
        {
            snapshot = events.ToList();
        }

        foreach (var evt in snapshot.TakeLast(24))
        {
            Console.WriteLine($"- [{evt.TimestampUtc:HH:mm:ss}] {evt.Source}: {evt.Message}");
            if (!string.IsNullOrWhiteSpace(evt.Prompt))
            {
                Console.WriteLine($"  prompt: {SummarizePrompt(evt.Prompt)}");
            }
        }
    }

    private static void RenderPromptsScreen(List<RuntimeProgressEvent> events)
    {
        Console.Clear();
        Console.WriteLine("=== Delegated Prompts ===");

        List<RuntimeProgressEvent> prompts;
        lock (events)
        {
            prompts = events.Where(e => !string.IsNullOrWhiteSpace(e.Prompt)).ToList();
        }

        if (prompts.Count == 0)
        {
            Console.WriteLine("No delegated prompts captured yet.");
            return;
        }

        foreach (var evt in prompts.TakeLast(20))
        {
            Console.WriteLine($"[{evt.TimestampUtc:HH:mm:ss}] {evt.Source}");
            Console.WriteLine(evt.Prompt);
            Console.WriteLine();
        }
    }

    private static void RenderArtefactsScreen(string runDirectory)
    {
        Console.Clear();
        Console.WriteLine("=== Artefacts ===");
        Console.WriteLine(runDirectory);
        Console.WriteLine();
        foreach (var file in Directory.GetFiles(runDirectory).OrderBy(Path.GetFileName))
        {
            Console.WriteLine($"- {Path.GetFileName(file)}");
        }
    }

    private static void RenderFileScreen(string title, string path, int maxLines)
    {
        Console.Clear();
        Console.WriteLine($"=== {title} ===");
        Console.WriteLine(path);
        Console.WriteLine();

        if (!File.Exists(path))
        {
            Console.WriteLine("File not found.");
            return;
        }

        foreach (var line in File.ReadLines(path).TakeLast(maxLines))
        {
            Console.WriteLine(line);
        }
    }

    private static void RenderFooter()
    {
        Console.WriteLine();
        Console.WriteLine("[1] Chat/Setup  [2] Run Monitor  [3] Logs  [4] Artefacts  [5] Review  [6] Prompts  [Q] Quit");
    }

    private static string SummarizePrompt(string prompt)
    {
        var trimmed = prompt.Replace(Environment.NewLine, " ").Trim();
        return trimmed.Length <= 120 ? trimmed : trimmed[..117] + "...";
    }

    private static void RenderAwaitingInputBanner(string? question)
    {
        Console.Clear();
        Console.WriteLine("=== Run Monitor (Awaiting Input) ===");
        Console.WriteLine("Agent requested clarification from user.");
        if (!string.IsNullOrWhiteSpace(question))
        {
            Console.WriteLine();
            Console.WriteLine(question);
        }

        Console.WriteLine();
        Console.WriteLine("Answer in the prompt below to continue...");
    }

    private static void RenderPreflightFailure(PreflightValidationResult result)
    {
        Console.Clear();
        Console.WriteLine("=== Startup Preflight Failed ===");
        Console.WriteLine(result.Summary);
        Console.WriteLine();
        Console.WriteLine("Actionable fixes:");
        foreach (var step in result.FixSteps)
        {
            Console.WriteLine($"- {step}");
        }

        Console.WriteLine();
        Console.WriteLine("Press any key to exit.");
        Console.ReadKey(intercept: true);
    }

    private static void RenderRunFailure(Exception ex)
    {
        Console.Clear();
        Console.WriteLine("=== Run Failed ===");
        Console.WriteLine(ex.Message);
        Console.WriteLine();
        Console.WriteLine("Hints:");
        Console.WriteLine("- If timeout mentions AwaitingUserInput=True, answer the active question prompt quickly.");
        Console.WriteLine("- If timeout repeats, increase copilot.sessionResponseTimeoutSeconds in appsettings.json.");
        Console.WriteLine("- If auth errors persist, run `copilot` then `/login`, then rerun ArchHarness.");
        Console.WriteLine();
        Console.WriteLine("Press any key to exit.");
        Console.ReadKey(intercept: true);
    }

    private enum Screen
    {
        ChatSetup,
        RunMonitor,
        Logs,
        Artefacts,
        Review,
        Prompts
    }
}
