namespace ArchHarness.App.Core;

/// <summary>
/// Identifies a specific run execution by its unique ID and artifact directory.
/// </summary>
/// <param name="RunId">The unique run identifier.</param>
/// <param name="RunDirectory">The directory where run artifacts are stored.</param>
public sealed record RunContext(string RunId, string RunDirectory);

/// <summary>
/// Provides access to the current run context for the executing async flow.
/// </summary>
public interface IRunContextAccessor
{
    /// <summary>Gets the current run context, or null if no run is active.</summary>
    RunContext? Current { get; }

    /// <summary>Sets or clears the current run context.</summary>
    /// <param name="context">The run context to set, or null to clear.</param>
    void SetCurrent(RunContext? context);
}

/// <summary>
/// AsyncLocal-backed implementation of <see cref="IRunContextAccessor"/>.
/// </summary>
public sealed class RunContextAccessor : IRunContextAccessor
{
    private static readonly AsyncLocal<RunContext?> CurrentContext = new AsyncLocal<RunContext?>();

    /// <inheritdoc />
    public RunContext? Current => CurrentContext.Value;

    /// <inheritdoc />
    public void SetCurrent(RunContext? context)
    {
        CurrentContext.Value = context;
    }
}
