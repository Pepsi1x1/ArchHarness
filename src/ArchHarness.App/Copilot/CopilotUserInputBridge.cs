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
            Console.WriteLine();
            Console.WriteLine("=== Agent Clarification Required ===");
            Console.WriteLine(request.Question);

            if (request.Choices is { Count: > 0 })
            {
                for (var i = 0; i < request.Choices.Count; i++)
                {
                    Console.WriteLine($"  [{i + 1}] {request.Choices[i]}");
                }
            }

            Console.Write("Your answer> ");
            var answer = Console.ReadLine();
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
}
