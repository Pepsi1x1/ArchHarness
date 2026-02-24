using ArchHarness.App.Agents;
using ArchHarness.App.Copilot;
using ArchHarness.App.Storage;
using ArchHarness.App.Workspace;

namespace ArchHarness.App.Core;

public sealed class OrchestratorRuntime
{
    private const string OrchestratorSource = "orchestrator";
    private readonly OrchestrationAgent _orchestrationAgent;
    private readonly FrontendAgent _frontendAgent;
    private readonly BuilderAgent _builderAgent;
    private readonly ArchitectureAgent _architectureAgent;
    private readonly IRunStore _runStore;
    private readonly IArtefactStore _artefactStore;
    private readonly ICopilotClient _copilotClient;
    private readonly ICopilotSessionEventStream _sessionEventStream;
    private readonly IBuildRunner _buildRunner;
    private readonly IRunContextAccessor _runContextAccessor;

    public OrchestratorRuntime(
        OrchestratorAgentDependencies agentDependencies,
        OrchestratorServiceDependencies serviceDependencies)
    {
        _orchestrationAgent = agentDependencies.OrchestrationAgent;
        _frontendAgent = agentDependencies.FrontendAgent;
        _builderAgent = agentDependencies.BuilderAgent;
        _architectureAgent = agentDependencies.ArchitectureAgent;
        _runStore = serviceDependencies.RunStore;
        _artefactStore = serviceDependencies.ArtefactStore;
        _copilotClient = serviceDependencies.CopilotClient;
        _sessionEventStream = serviceDependencies.SessionEventStream;
        _buildRunner = serviceDependencies.BuildRunner;
        _runContextAccessor = serviceDependencies.RunContextAccessor;
    }

    public async Task<RunArtefacts> RunAsync(
        RunRequest request,
        IProgress<RuntimeProgressEvent>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var adapter = WorkspaceAdapterFactory.Create(request.WorkspaceMode, request.WorkspacePath);
        var initGit = request.WorkspaceMode is "new-project" or "existing-git";
        await adapter.InitializeAsync(request.WorkspaceMode == "new-project" ? request.ProjectName : null, initGit, cancellationToken);

        var initialBuildSelection = BuildCommandInference.Select(
            adapter.RootPath,
            request.BuildCommand,
            request.WorkspaceMode,
            request.ProjectName);
        if (!string.Equals(initialBuildSelection.Command, request.BuildCommand, StringComparison.Ordinal))
        {
            request = request with { BuildCommand = initialBuildSelection.Command };
        }

        var runDirectory = _runStore.CreateRunDirectory(adapter.RootPath);
        var runId = Path.GetFileName(runDirectory);
        _runContextAccessor.SetCurrent(new RunContext(runId, runDirectory));
        using var sessionEventCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var sessionEventPump = Task.Run(async () => await PumpSessionEventsAsync(runDirectory, runId, sessionEventCts.Token), CancellationToken.None);

        try
        {
            await _artefactStore.AppendEventAsync(runDirectory, new { runId, source = OrchestratorSource, message = "Run started" }, cancellationToken);
            await _artefactStore.AppendEventAsync(runDirectory, new
            {
                runId,
                source = "request",
                message = "Run request received",
                taskPrompt = request.TaskPrompt,
                workspacePath = request.WorkspacePath,
                workspaceMode = request.WorkspaceMode,
                workflow = request.Workflow,
                projectName = request.ProjectName,
                buildCommand = request.BuildCommand,
                modelOverrides = request.ModelOverrides
            }, cancellationToken);
            await _artefactStore.AppendEventAsync(runDirectory, new
            {
                runId,
                source = "build-selection",
                message = "Initial build command selection",
                buildCommand = request.BuildCommand,
                inferred = initialBuildSelection.Inferred,
                reason = initialBuildSelection.Reason
            }, cancellationToken);
            progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, OrchestratorSource, "Run started"));
            var plan = await _orchestrationAgent.BuildExecutionPlanAsync(
                request,
                adapter.RootPath,
                _orchestrationAgent.Id,
                _orchestrationAgent.Role,
                cancellationToken);
            await _artefactStore.WriteExecutionPlanAsync(runDirectory, plan, cancellationToken);

        string frontendPlan = string.Empty;
        IReadOnlyList<string> filesTouched = Array.Empty<string>();
        ArchitectureReview review = new(Array.Empty<ArchitectureFinding>(), Array.Empty<string>());

        Dictionary<string, Func<ExecutionPlanStep, Task>> agentStrategies = new Dictionary<string, Func<ExecutionPlanStep, Task>>
        {
            ["Frontend"] = async (ExecutionPlanStep s) =>
            {
                frontendPlan = await _frontendAgent.CreatePlanAsync(
                    adapter,
                    s.Objective,
                    request.ModelOverrides,
                    _frontendAgent.Id,
                    _frontendAgent.Role,
                    cancellationToken);
            },
            ["Builder"] = async (ExecutionPlanStep s) =>
            {
                filesTouched = await _builderAgent.ImplementAsync(
                    adapter,
                    s.Objective,
                    request.ModelOverrides,
                    null,
                    _builderAgent.Id,
                    _builderAgent.Role,
                    cancellationToken);
            },
            ["Architecture"] = async (ExecutionPlanStep s) =>
            {
                review = await _architectureAgent.ReviewAsync(
                    s.Objective,
                    await adapter.DiffAsync(cancellationToken),
                    adapter.RootPath,
                    filesTouched,
                    s.Languages,
                    request.ModelOverrides,
                    _architectureAgent.Id,
                    _architectureAgent.Role,
                    cancellationToken);
            }
        };

        var pendingSteps = plan.Steps.ToDictionary(s => s.Id);
        var completedStepIds = new HashSet<int>();
        while (pendingSteps.Count > 0)
        {
            var step = pendingSteps.Values
                .OrderBy(s => s.Id)
                .FirstOrDefault(s => DependenciesSatisfied(s, completedStepIds, pendingSteps));

            if (step is null)
            {
                step = pendingSteps.Values.OrderBy(s => s.Id).First();
                await _artefactStore.AppendEventAsync(runDirectory, new
                {
                    runId,
                    source = OrchestratorSource,
                    message = $"Dependency deadlock detected; force-executing step {step.Id}."
                }, cancellationToken);
            }

            await _artefactStore.AppendEventAsync(runDirectory, new { runId, source = step.Agent, message = step.Objective }, cancellationToken);
            progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, step.Agent, "Delegated prompt started", step.Objective));
            if (!agentStrategies.TryGetValue(step.Agent, out Func<ExecutionPlanStep, Task>? strategy))
            {
                throw new InvalidOperationException($"Unrecognized agent role: '{step.Agent}'.");
            }

            await strategy(step);

                    completedStepIds.Add(step.Id);
                    pendingSteps.Remove(step.Id);
        }

        var iteration = 0;
         var architectureLanguages = plan.Steps.LastOrDefault(s => s.Agent == "Architecture")?.Languages;
        while (plan.IterationStrategy.ReviewRequired &&
               review.Findings.Any(f => f.Severity.Equals("high", StringComparison.OrdinalIgnoreCase)) &&
               iteration < plan.IterationStrategy.MaxIterations)
        {
            iteration++;
            progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, "architecture-loop", $"Review iteration {iteration}"));
            var remediationPrompt = await _orchestrationAgent.BuildRemediationPromptAsync(
                request,
                adapter.RootPath,
                review,
                iteration,
                _orchestrationAgent.Id,
                _orchestrationAgent.Role,
                cancellationToken);
            progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, "Architecture", "Enforcement prompt started", remediationPrompt));

            var latestDiff = await adapter.DiffAsync(cancellationToken);
            review = await _architectureAgent.ReviewAsync(
                remediationPrompt,
                latestDiff,
                adapter.RootPath,
                filesTouched,
                architectureLanguages,
                request.ModelOverrides,
                _architectureAgent.Id,
                _architectureAgent.Role,
                cancellationToken);

            // Refresh touched files snapshot after architecture enforcement pass.
            filesTouched = ParseTouchedFiles(latestDiff);
        }

        await _artefactStore.WriteArchitectureReviewAsync(runDirectory, review, cancellationToken);

        var finalBuildSelection = BuildCommandInference.Select(
            adapter.RootPath,
            request.BuildCommand,
            request.WorkspaceMode,
            request.ProjectName);
        await _artefactStore.AppendEventAsync(runDirectory, new
        {
            runId,
            source = "build-selection",
            message = "Final build command selection",
            buildCommand = finalBuildSelection.Command,
            inferred = finalBuildSelection.Inferred,
            reason = finalBuildSelection.Reason
        }, cancellationToken);

        var buildResult = await _buildRunner.RunAsync(finalBuildSelection.Command, adapter.RootPath, cancellationToken);
        await _artefactStore.WriteBuildResultAsync(runDirectory, buildResult, cancellationToken);
        progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, "build", buildResult.Passed ? "Build passed" : "Build failed or not executed"));

        var buildCommandConfigured = !string.IsNullOrWhiteSpace(request.BuildCommand);
        var completed = await _orchestrationAgent.ValidateCompletionAsync(
            plan,
            review,
            buildResult.Passed,
            buildCommandConfigured,
            request.ModelOverrides,
            _orchestrationAgent.Id,
            _orchestrationAgent.Role,
            cancellationToken);
        var summary = $"""
            # Final Summary
            - Completed: {completed}
            - FrontendPlan: {frontendPlan}
            - FilesTouched: {string.Join(", ", filesTouched)}
            - BuildExecuted: {buildResult.Executed}
            - BuildPassed: {buildResult.Passed}
            """;
        await _artefactStore.WriteFinalSummaryAsync(runDirectory, summary, cancellationToken);

        var modelOverrides = request.ModelOverrides?.Select(pair => $"{pair.Key}={pair.Value}").ToArray() ?? Array.Empty<string>();
        var usage = _copilotClient.GetUsageSnapshot();

        await _runStore.WriteRunLogAsync(runDirectory, new
        {
            status = completed ? "completed" : "incomplete",
            request.WorkspaceMode,
            request.Workflow,
            modelOverrides,
            agents = new[]
            {
                new { role = "orchestration", model = _orchestrationAgent.ResolveModel(request.ModelOverrides) },
                new { role = "frontend", model = _frontendAgent.ResolveModel(request.ModelOverrides) },
                new { role = "builder", model = _builderAgent.ResolveModel(request.ModelOverrides) },
                new { role = "architecture", model = _architectureAgent.ResolveModel(request.ModelOverrides) }
            },
            copilotUsage = usage
        }, cancellationToken);

            await _artefactStore.AppendEventAsync(runDirectory, new { runId, source = OrchestratorSource, message = "Run completed" }, cancellationToken);
            progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, OrchestratorSource, "Run completed"));

            await sessionEventCts.CancelAsync();
            await sessionEventPump;
            return new RunArtefacts(runId, runDirectory);
        }
        finally
        {
            _runContextAccessor.SetCurrent(null);
        }
    }

    private async Task PumpSessionEventsAsync(string runDirectory, string runId, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in _sessionEventStream.ReadAllAsync(cancellationToken))
            {
                await _artefactStore.AppendEventAsync(runDirectory, new
                {
                    runId,
                    source = "copilot.session",
                    eventType = evt.EventType,
                    sessionId = evt.SessionId,
                    model = evt.Model,
                    details = evt.Details,
                    timestampUtc = evt.TimestampUtc
                }, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on run shutdown when stopping event pump.
        }
    }

    private static IReadOnlyList<string> ParseTouchedFiles(string? diff)
    {
        if (string.IsNullOrWhiteSpace(diff))
        {
            return Array.Empty<string>();
        }

        return diff
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool DependenciesSatisfied(
        ExecutionPlanStep step,
        ISet<int> completedStepIds,
        IReadOnlyDictionary<int, ExecutionPlanStep> pendingSteps)
    {
        if (step.DependsOnStepIds is null || step.DependsOnStepIds.Count == 0)
        {
            return true;
        }

        foreach (var dep in step.DependsOnStepIds)
        {
            if (pendingSteps.ContainsKey(dep))
            {
                return false;
            }

            if (!completedStepIds.Contains(dep))
            {
                return false;
            }
        }

        return true;
    }

    public sealed class OrchestratorAgentDependencies
    {
        public OrchestratorAgentDependencies(
            OrchestrationAgent orchestrationAgent,
            FrontendAgent frontendAgent,
            BuilderAgent builderAgent,
            ArchitectureAgent architectureAgent)
        {
            OrchestrationAgent = orchestrationAgent;
            FrontendAgent = frontendAgent;
            BuilderAgent = builderAgent;
            ArchitectureAgent = architectureAgent;
        }

        public OrchestrationAgent OrchestrationAgent { get; }
        public FrontendAgent FrontendAgent { get; }
        public BuilderAgent BuilderAgent { get; }
        public ArchitectureAgent ArchitectureAgent { get; }
    }

    public sealed class OrchestratorServiceDependencies
    {
        public OrchestratorServiceDependencies(
            IRunStore runStore,
            IArtefactStore artefactStore,
            ICopilotClient copilotClient,
            ICopilotSessionEventStream sessionEventStream,
            IBuildRunner buildRunner,
            IRunContextAccessor runContextAccessor)
        {
            RunStore = runStore;
            ArtefactStore = artefactStore;
            CopilotClient = copilotClient;
            SessionEventStream = sessionEventStream;
            BuildRunner = buildRunner;
            RunContextAccessor = runContextAccessor;
        }

        public IRunStore RunStore { get; }
        public IArtefactStore ArtefactStore { get; }
        public ICopilotClient CopilotClient { get; }
        public ICopilotSessionEventStream SessionEventStream { get; }
        public IBuildRunner BuildRunner { get; }
        public IRunContextAccessor RunContextAccessor { get; }
    }

}
