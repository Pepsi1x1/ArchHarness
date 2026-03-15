using GitHub.Copilot.SDK;

namespace ArchHarness.App.Copilot;

/// <summary>
/// Tracks whether the system is currently awaiting user input during a Copilot session.
/// </summary>
public interface IUserInputState
{
    /// <summary>Gets whether user input is currently being awaited.</summary>
    bool IsAwaitingInput { get; }

    /// <summary>Gets the active question text, or null if none.</summary>
    string? ActiveQuestion { get; }

    /// <summary>
    /// Marks the state as awaiting input with the specified question.
    /// </summary>
    /// <param name="question">The question being asked, or null.</param>
    void SetAwaiting(string? question);

    /// <summary>Clears the awaiting-input state.</summary>
    void Clear();
}

/// <summary>
/// Thread-safe implementation of <see cref="IUserInputState"/>.
/// </summary>
public sealed class UserInputState : IUserInputState
{
    private readonly object _sync = new object();
    private bool _awaiting;
    private string? _question;

    /// <inheritdoc />
    public bool IsAwaitingInput
    {
        get { lock (this._sync) { return this._awaiting; } }
    }

    /// <inheritdoc />
    public string? ActiveQuestion
    {
        get { lock (this._sync) { return this._question; } }
    }

    /// <inheritdoc />
    public void SetAwaiting(string? question)
    {
        lock (this._sync)
        {
            this._awaiting = true;
            this._question = question;
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (this._sync)
        {
            this._awaiting = false;
            this._question = null;
        }
    }
}

/// <summary>
/// Bridges Copilot agent user-input requests to the host application's input mechanism.
/// </summary>
public interface ICopilotUserInputBridge
{
    /// <summary>
    /// Requests user input synchronously and returns the response.
    /// </summary>
    /// <param name="request">The user input request.</param>
    /// <returns>The user input response.</returns>
    Task<UserInputResponse> RequestInputAsync(UserInputRequest request);
}

/// <summary>
/// Console-based implementation of <see cref="ICopilotUserInputBridge"/> that renders questions in the terminal.
/// </summary>
public sealed class ConsoleCopilotUserInputBridge : ICopilotUserInputBridge
{
    private readonly IUserInputState _state;
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

    /// <summary>
    /// Initializes a new instance of <see cref="ConsoleCopilotUserInputBridge"/>.
    /// </summary>
    /// <param name="state">The shared user input state tracker.</param>
    public ConsoleCopilotUserInputBridge(IUserInputState state)
    {
        this._state = state;
    }

    /// <inheritdoc />
    public async Task<UserInputResponse> RequestInputAsync(UserInputRequest request)
    {
        await this._gate.WaitAsync();
        try
        {
            this._state.SetAwaiting(request.Question);
            int width = Math.Max(60, Console.WindowWidth - 1);
            int maxRow = Math.Max(0, Console.WindowHeight - 1);
            int startRow = Math.Min(Console.CursorTop + 1, maxRow);

            WriteLineAt(startRow++, "=== Agent Clarification Required ===", width, ConsoleColor.Yellow);
            WriteLineAt(startRow++, request.Question ?? string.Empty, width, ConsoleColor.White);

            if (request.Choices is { Count: > 0 })
            {
                for (int i = 0; i < request.Choices.Count; i++)
                {
                    WriteLineAt(startRow++, $"  [{i + 1}] {request.Choices[i]}", width, ConsoleColor.Gray);
                }
            }

            int promptRow = startRow;
            string promptLabel = "Your answer> ";
            WriteLineAt(promptRow, promptLabel, width, ConsoleColor.Cyan);

            bool restoreCursor = TryGetCursorVisible();
            TrySetCursorVisible(true);
            int maxColumn = Math.Max(0, width - 1);
            int cursorColumn = Math.Min(promptLabel.Length, maxColumn);
            Console.SetCursorPosition(cursorColumn, promptRow);
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
            this._state.Clear();
            this._gate.Release();
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
