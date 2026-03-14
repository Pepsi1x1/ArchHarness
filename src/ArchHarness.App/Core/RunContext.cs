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
    private static readonly AsyncLocal<RunContext?> CURRENT_CONTEXT = new AsyncLocal<RunContext?>();

    /// <inheritdoc />
    public RunContext? Current => CURRENT_CONTEXT.Value;

    /// <inheritdoc />
    public void SetCurrent(RunContext? context)
    {
        CURRENT_CONTEXT.Value = context;
    }
}

/// <summary>
/// Provides access to the current workspace root for the executing async flow.
/// </summary>
public interface IWorkspaceRootAccessor
{
    /// <summary>Gets the current workspace root, or null if no workspace is active.</summary>
    string? Current { get; }

    /// <summary>Sets or clears the current workspace root.</summary>
    /// <param name="workspaceRoot">The workspace root to set, or null to clear.</param>
    void SetCurrent(string? workspaceRoot);
}

/// <summary>
/// AsyncLocal-backed implementation of <see cref="IWorkspaceRootAccessor"/>.
/// </summary>
public sealed class WorkspaceRootAccessor : IWorkspaceRootAccessor
{
    private static readonly AsyncLocal<string?> CURRENT_WORKSPACE_ROOT = new AsyncLocal<string?>();

    /// <inheritdoc />
    public string? Current => CURRENT_WORKSPACE_ROOT.Value;

    /// <inheritdoc />
    public void SetCurrent(string? workspaceRoot)
    {
        CURRENT_WORKSPACE_ROOT.Value = workspaceRoot;
    }
}
