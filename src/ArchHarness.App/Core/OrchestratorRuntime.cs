using ArchHarness.App.Constants;
using ArchHarness.App.Storage;
using ArchHarness.App.Workspace;

namespace ArchHarness.App.Core;

/// <summary>
/// Bootstraps workspaces and delegates full run execution to focused collaborators.
/// </summary>
public sealed class OrchestratorRuntime : IOrchestratorRuntime
{
    private readonly IOrchestratedRunProcessor _runProcessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrchestratorRuntime"/> class.
    /// </summary>
    public OrchestratorRuntime(IOrchestratedRunProcessor runProcessor)
    {
        this._runProcessor = runProcessor;
    }

    /// <inheritdoc />
    public async Task<RunArtefacts> RunAsync(
        RunRequest request,
        IProgress<RuntimeProgressEvent>? progress = null,
        Action<string, string>? onRunContextEstablished = null,
        CancellationToken cancellationToken = default)
    {
        request = RunRequestWorkflowDefaults.Apply(request);
        IWorkspaceAdapter adapter = WorkspaceAdapterFactory.Create(request.WorkspaceMode, request.WorkspacePath);
        bool initGit = request.WorkspaceMode is "new-project" or "existing-git";
        await adapter.InitializeAsync(request.WorkspaceMode == "new-project" ? request.ProjectName : null, initGit, cancellationToken).ConfigureAwait(false);

        BuildCommandSelection? initialBuildSelection = null;
        if (!string.Equals(request.Workflow, WorkflowNames.WIKIDOC, StringComparison.OrdinalIgnoreCase))
        {
            initialBuildSelection = BuildCommandInference.Select(
                adapter.RootPath,
                request.BuildCommand,
                request.WorkspaceMode,
                request.ProjectName);
            if (!string.Equals(initialBuildSelection.Command, request.BuildCommand, StringComparison.Ordinal))
            {
                request = request with { BuildCommand = initialBuildSelection.Command };
            }
        }

        OrchestratedRunContext runContext = new OrchestratedRunContext(adapter, request, null, initialBuildSelection);
        return await this._runProcessor.ExecuteAsync(
            runContext,
            progress,
            onRunContextEstablished,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<RunArtefacts> ResumeAsync(
        PersistedRunState runState,
        IProgress<RuntimeProgressEvent>? progress = null,
        Action<string, string>? onRunContextEstablished = null,
        CancellationToken cancellationToken = default)
    {
        runState = runState with { Request = RunRequestWorkflowDefaults.Apply(runState.Request) };
        string resumeWorkspaceMode = Directory.Exists(Path.Combine(runState.WorkspaceRoot, ".git"))
            ? "existing-git"
            : "existing-folder";
        IWorkspaceAdapter adapter = WorkspaceAdapterFactory.Create(resumeWorkspaceMode, runState.WorkspaceRoot);
        await adapter.InitializeAsync(null, resumeWorkspaceMode == "existing-git", cancellationToken).ConfigureAwait(false);

        OrchestratedRunContext resumeContext = new OrchestratedRunContext(adapter, runState.Request, runState, null);
        return await this._runProcessor.ExecuteAsync(
            resumeContext,
            progress,
            onRunContextEstablished,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Groups the run-phase collaborators (plan execution and architecture review)
    /// to reduce constructor over-injection in run-processing services.
    /// </summary>
    public sealed class RunPhaseDependencies
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RunPhaseDependencies"/> class.
        /// </summary>
        public RunPhaseDependencies(IArchitectureReviewLoop architectureReviewLoop, IPlanExecutor planExecutor)
        {
            this.ArchitectureReviewLoop = architectureReviewLoop;
            this.PlanExecutor = planExecutor;
        }

        /// <summary>Gets the architecture review iteration loop.</summary>
        public IArchitectureReviewLoop ArchitectureReviewLoop { get; }

        /// <summary>Gets the execution plan builder and dispatcher.</summary>
        public IPlanExecutor PlanExecutor { get; }
    }
}
