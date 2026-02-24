namespace ArchHarness.App.Core;

public sealed record RunContext(string RunId, string RunDirectory);

public interface IRunContextAccessor
{
    RunContext? Current { get; }
    void SetCurrent(RunContext? context);
}

public sealed class RunContextAccessor : IRunContextAccessor
{
    private static readonly AsyncLocal<RunContext?> CurrentContext = new();

    public RunContext? Current => CurrentContext.Value;

    public void SetCurrent(RunContext? context)
    {
        CurrentContext.Value = context;
    }
}
