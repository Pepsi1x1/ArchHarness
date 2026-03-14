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

/// <summary>
/// Provides access to the current permission handler mode for the executing async flow.
/// </summary>
public interface IPermissionHandlerModeAccessor
{
    /// <summary>Gets the current permission handler mode, or null if unset.</summary>
    string? Current { get; }

    /// <summary>Sets or clears the current permission handler mode.</summary>
    /// <param name="mode">The permission handler mode to set, or null to clear.</param>
    void SetCurrent(string? mode);
}

/// <summary>
/// AsyncLocal-backed implementation of <see cref="IPermissionHandlerModeAccessor"/>.
/// </summary>
public sealed class PermissionHandlerModeAccessor : IPermissionHandlerModeAccessor
{
    private static readonly AsyncLocal<string?> CURRENT_PERMISSION_HANDLER_MODE = new AsyncLocal<string?>();

    /// <inheritdoc />
    public string? Current => CURRENT_PERMISSION_HANDLER_MODE.Value;

    /// <inheritdoc />
    public void SetCurrent(string? mode)
    {
        CURRENT_PERMISSION_HANDLER_MODE.Value = mode;
    }
}

/// <summary>
/// Provides access to the current review-loop agent selection for the executing async flow.
/// </summary>
public interface IReviewLoopAgentSelectionAccessor
{
    /// <summary>Gets the current review-loop agent selection, or null if unset.</summary>
    ReviewLoopAgentSelection? Current { get; }

    /// <summary>Sets or clears the current review-loop agent selection.</summary>
    /// <param name="selection">The review-loop agent selection to set, or null to clear.</param>
    void SetCurrent(ReviewLoopAgentSelection? selection);
}

/// <summary>
/// AsyncLocal-backed implementation of <see cref="IReviewLoopAgentSelectionAccessor"/>.
/// </summary>
public sealed class ReviewLoopAgentSelectionAccessor : IReviewLoopAgentSelectionAccessor
{
    private static readonly AsyncLocal<ReviewLoopAgentSelection?> CURRENT_REVIEW_LOOP_AGENT_SELECTION = new AsyncLocal<ReviewLoopAgentSelection?>();

    /// <inheritdoc />
    public ReviewLoopAgentSelection? Current => CURRENT_REVIEW_LOOP_AGENT_SELECTION.Value;

    /// <inheritdoc />
    public void SetCurrent(ReviewLoopAgentSelection? selection)
    {
        CURRENT_REVIEW_LOOP_AGENT_SELECTION.Value = selection;
    }
}
