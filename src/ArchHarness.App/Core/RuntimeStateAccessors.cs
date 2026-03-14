namespace ArchHarness.App.Core;

/// <summary>
/// Groups the async-local runtime state accessors that are commonly injected together
/// into orchestration and controller classes, reducing constructor over-injection.
/// </summary>
public sealed class RuntimeStateAccessors
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RuntimeStateAccessors"/> class.
    /// </summary>
    /// <param name="permissionHandlerMode">Accessor for the current permission handler mode.</param>
    /// <param name="reviewLoopAgentSelection">Accessor for the current review-loop agent selection.</param>
    /// <param name="workspaceRoot">Accessor for the current workspace root path.</param>
    public RuntimeStateAccessors(
        IPermissionHandlerModeAccessor permissionHandlerMode,
        IReviewLoopAgentSelectionAccessor reviewLoopAgentSelection,
        IWorkspaceRootAccessor workspaceRoot)
    {
        this.PermissionHandlerMode = permissionHandlerMode;
        this.ReviewLoopAgentSelection = reviewLoopAgentSelection;
        this.WorkspaceRoot = workspaceRoot;
    }

    /// <summary>Gets the permission handler mode accessor.</summary>
    public IPermissionHandlerModeAccessor PermissionHandlerMode { get; }

    /// <summary>Gets the review-loop agent selection accessor.</summary>
    public IReviewLoopAgentSelectionAccessor ReviewLoopAgentSelection { get; }

    /// <summary>Gets the workspace root accessor.</summary>
    public IWorkspaceRootAccessor WorkspaceRoot { get; }
}
