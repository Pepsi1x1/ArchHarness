using ArchHarness.App.Agents;
using ArchHarness.App.Storage;
using ArchHarness.App.Workspace;

namespace ArchHarness.App.Core;

public sealed class OrchestratorRuntime
{
    private readonly OrchestrationAgent _orchestrationAgent;
    private readonly FrontendAgent _frontendAgent;
    private readonly BuilderAgent _builderAgent;
    private readonly ArchitectureAgent _architectureAgent;
    private readonly IRunStore _runStore;
    private readonly IArtefactStore _artefactStore;

    public OrchestratorRuntime(
        OrchestrationAgent orchestrationAgent,
        FrontendAgent frontendAgent,
        BuilderAgent builderAgent,
        ArchitectureAgent architectureAgent,
        IRunStore runStore,
        IArtefactStore artefactStore)
    {
        _orchestrationAgent = orchestrationAgent;
        _frontendAgent = frontendAgent;
        _builderAgent = builderAgent;
        _architectureAgent = architectureAgent;
        _runStore = runStore;
        _artefactStore = artefactStore;
    }

    public async Task<RunArtefacts> RunAsync(RunRequest request, CancellationToken cancellationToken = default)
    {
        var adapter = WorkspaceAdapterFactory.Create(request.WorkspaceMode, request.WorkspacePath);
        var initGit = request.WorkspaceMode is "new-project" or "existing-git";
        await adapter.InitializeAsync(request.WorkspaceMode == "new-project" ? request.ProjectName : null, initGit, cancellationToken);

        var runDirectory = _runStore.CreateRunDirectory(adapter.RootPath);
        var runId = Path.GetFileName(runDirectory);

        await _artefactStore.AppendEventAsync(runDirectory, new { runId, source = "orchestrator", message = "Run started" }, cancellationToken);
        var plan = await _orchestrationAgent.BuildExecutionPlanAsync(request, cancellationToken);
        await _artefactStore.WriteExecutionPlanAsync(runDirectory, plan, cancellationToken);

        string frontendPlan = string.Empty;
        IReadOnlyList<string> filesTouched = Array.Empty<string>();
        ArchitectureReview review = new(Array.Empty<ArchitectureFinding>(), Array.Empty<string>());

        foreach (var step in plan.Steps)
        {
            await _artefactStore.AppendEventAsync(runDirectory, new { runId, source = step.Agent, message = step.Objective }, cancellationToken);
            switch (step.Agent)
            {
                case "Frontend":
                    frontendPlan = await _frontendAgent.CreatePlanAsync(request.TaskPrompt, cancellationToken);
                    break;
                case "Builder":
                    filesTouched = await _builderAgent.ImplementAsync(adapter, request.TaskPrompt, cancellationToken: cancellationToken);
                    break;
                case "Architecture":
                    review = await _architectureAgent.ReviewAsync(await adapter.DiffAsync(cancellationToken), filesTouched, cancellationToken);
                    break;
            }
        }

        var iteration = 0;
        while (plan.IterationStrategy.ReviewRequired &&
               review.Findings.Any(f => f.Severity.Equals("high", StringComparison.OrdinalIgnoreCase)) &&
               iteration < plan.IterationStrategy.MaxIterations)
        {
            iteration++;
            filesTouched = await _builderAgent.ImplementAsync(adapter, request.TaskPrompt, review.RequiredActions, cancellationToken);
            review = await _architectureAgent.ReviewAsync(await adapter.DiffAsync(cancellationToken), filesTouched, cancellationToken);
        }

        await _artefactStore.WriteArchitectureReviewAsync(runDirectory, review, cancellationToken);
        var completed = await _orchestrationAgent.ValidateCompletionAsync(review, cancellationToken);
        var summary = $"""
            # Final Summary
            - Completed: {completed}
            - FrontendPlan: {frontendPlan}
            - FilesTouched: {string.Join(", ", filesTouched)}
            """;
        await _artefactStore.WriteFinalSummaryAsync(runDirectory, summary, cancellationToken);

        await _runStore.WriteRunLogAsync(runDirectory, new
        {
            status = completed ? "completed" : "incomplete",
            request.WorkspaceMode,
            request.Workflow,
            agents = new[]
            {
                new { role = "orchestration", model = _orchestrationAgent.Model },
                new { role = "frontend", model = _frontendAgent.Model },
                new { role = "builder", model = _builderAgent.Model },
                new { role = "architecture", model = _architectureAgent.Model }
            }
        }, cancellationToken);

        await _artefactStore.AppendEventAsync(runDirectory, new { runId, source = "orchestrator", message = "Run completed" }, cancellationToken);
        return new RunArtefacts(runId, runDirectory);
    }
}
