using GitHub.Copilot.SDK;

namespace ArchHarness.App.Copilot;

public interface IUserInputState
{
    bool IsAwaitingInput { get; }
    string? ActiveQuestion { get; }
    void SetAwaiting(string? question);
    void Clear();
}

public sealed class UserInputState : IUserInputState
{
    private readonly object _sync = new();
    private bool _awaiting;
    private string? _question;

    public bool IsAwaitingInput
    {
        get { lock (_sync) { return _awaiting; } }
    }

    public string? ActiveQuestion
    {
        get { lock (_sync) { return _question; } }
    }

    public void SetAwaiting(string? question)
    {
        lock (_sync)
        {
            _awaiting = true;
            _question = question;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _awaiting = false;
            _question = null;
        }
    }
}

public interface ICopilotUserInputBridge
{
    Task<UserInputResponse> RequestInputAsync(UserInputRequest request);
}

public sealed class ConsoleCopilotUserInputBridge : ICopilotUserInputBridge
{
    private readonly IUserInputState _state;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ConsoleCopilotUserInputBridge(IUserInputState state)
    {
        _state = state;
    }

    public async Task<UserInputResponse> RequestInputAsync(UserInputRequest request)
    {
        await _gate.WaitAsync();
        try
        {
            _state.SetAwaiting(request.Question);
            int width = Math.Max(60, Console.WindowWidth - 1);
            int startRow = Math.Min(Console.CursorTop + 1, Math.Max(0, Console.WindowHeight - 1));

            WriteLineAt(startRow++, "=== Agent Clarification Required ===", width, ConsoleColor.Yellow);
            WriteLineAt(startRow++, request.Question ?? string.Empty, width, ConsoleColor.White);

            if (request.Choices is { Count: > 0 })
            {
                for (var i = 0; i < request.Choices.Count; i++)
                {
                    WriteLineAt(startRow++, $"  [{i + 1}] {request.Choices[i]}", width, ConsoleColor.Gray);
                }
            }

            int promptRow = startRow;
            string promptLabel = "Your answer> ";
            WriteLineAt(promptRow, promptLabel, width, ConsoleColor.Cyan);

            bool restoreCursor = TryGetCursorVisible();
            TrySetCursorVisible(true);
            Console.SetCursorPosition(Math.Min(promptLabel.Length, Math.Max(0, width - 1)), promptRow);
            string? answer;
            try
            {
                answer = TryReadLine();
            }
            finally
            {
                TrySetCursorVisible(restoreCursor);
            }

            if (string.IsNullOrWhiteSpace(answer) && request.Choices is { Count: > 0 })
            {
                answer = request.Choices[0];
            }

            return new UserInputResponse
            {
                Answer = answer ?? string.Empty,
                WasFreeform = true
            };
        }
        finally
        {
            _state.Clear();
            _gate.Release();
        }
    }

    private static void WriteLineAt(int row, string text, int width, ConsoleColor color)
    {
        Console.SetCursorPosition(0, row);
        Console.ForegroundColor = color;
        string output = text.Length > width ? text[..width] : text;
        Console.Write(output.PadRight(width));
        Console.ResetColor();
    }

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
