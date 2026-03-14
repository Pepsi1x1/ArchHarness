using ArchHarness.App.Core;
using GitHub.Copilot.SDK;

namespace ArchHarness.App.Copilot;

/// <summary>
/// Handles interactive console-based permission prompts for Copilot SDK operations.
/// Separated from session lifecycle management to isolate console I/O concerns.
/// </summary>
public sealed class InteractivePermissionPromptHandler : ICopilotPermissionPromptHandler
{
    private readonly IUserInputState _userInputState;
    private readonly SemaphoreSlim _permissionPromptGate = new SemaphoreSlim(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="InteractivePermissionPromptHandler"/> class.
    /// </summary>
    /// <param name="userInputState">Tracks whether the agent is awaiting user input.</param>
    public InteractivePermissionPromptHandler(IUserInputState userInputState)
    {
        this._userInputState = userInputState;
    }

    /// <summary>
    /// Prompts the user interactively via the console to approve or deny a Copilot permission request.
    /// </summary>
    /// <param name="request">The permission request details from the SDK.</param>
    /// <param name="invocation">The invocation context for the permission request.</param>
    /// <returns>The user's approval or denial decision.</returns>
    public async Task<PermissionRequestResult> HandleAsync(PermissionRequest request, PermissionInvocation invocation)
    {
        if (Console.IsInputRedirected)
        {
            return CreatePermissionResult(PermissionRequestResultKind.DeniedCouldNotRequestFromUser);
        }

        await this._permissionPromptGate.WaitAsync().ConfigureAwait(false);
        try
        {
            string question = PermissionPromptFormatter.BuildQuestion(request, invocation);
            this._userInputState.SetAwaiting(question);

            int width = Math.Max(60, Console.WindowWidth - 1);
            int row = Math.Min(Console.CursorTop + 1, Math.Max(0, Console.WindowHeight - 1));

            WritePromptLine(row++, "=== Permission Approval Required ===", width, ConsoleColor.Yellow);
            foreach (string line in WrapPromptText(question, width))
            {
                WritePromptLine(row++, line, width, ConsoleColor.White);
            }

            const string PROMPT_LABEL = "Approve? [y/N] ";
            WritePromptLine(row, PROMPT_LABEL, width, ConsoleColor.Cyan);

            bool restoreCursor = TryGetCursorVisible();
            TrySetCursorVisible(true);
            Console.SetCursorPosition(Math.Min(PROMPT_LABEL.Length, Math.Max(0, width - 1)), row);

            string? answer;
            try
            {
                answer = TryReadLine();
            }
            finally
            {
                TrySetCursorVisible(restoreCursor);
            }

            return IsApprovalAnswer(answer)
                ? CreatePermissionResult(PermissionRequestResultKind.Approved)
                : CreatePermissionResult(PermissionRequestResultKind.DeniedInteractivelyByUser);
        }
        finally
        {
            this._userInputState.Clear();
            this._permissionPromptGate.Release();
        }
    }

    private static PermissionRequestResult CreatePermissionResult(PermissionRequestResultKind kind)
        => new PermissionRequestResult { Kind = kind };

    private static IEnumerable<string> WrapPromptText(string text, int width)
    {
        foreach (string rawLine in text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            string remaining = rawLine;
            if (remaining.Length == 0)
            {
                yield return string.Empty;
                continue;
            }

            while (remaining.Length > width)
            {
                yield return remaining[..width];
                remaining = remaining[width..];
            }

            yield return remaining;
        }
    }

    private static void WritePromptLine(int row, string text, int width, ConsoleColor color)
    {
        Console.SetCursorPosition(0, Math.Min(row, Math.Max(0, Console.WindowHeight - 1)));
        Console.ForegroundColor = color;
        string output = text.Length > width ? text[..width] : text;
        Console.Write(output.PadRight(width));
        Console.ResetColor();
    }

    private static bool IsApprovalAnswer(string? answer)
        => !string.IsNullOrWhiteSpace(answer)
            && (answer.Equals("y", StringComparison.OrdinalIgnoreCase)
                || answer.Equals("yes", StringComparison.OrdinalIgnoreCase));

    private static bool TryGetCursorVisible()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            return Console.CursorVisible;
        }
        catch
        {
            return false;
        }
    }

    private static void TrySetCursorVisible(bool visible)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            Console.CursorVisible = visible;
        }
        catch
        {
            // Ignore terminal capability failures and continue with input flow.
        }
    }

    private static string? TryReadLine()
    {
        try
        {
            return Console.ReadLine();
        }
        catch (IOException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
