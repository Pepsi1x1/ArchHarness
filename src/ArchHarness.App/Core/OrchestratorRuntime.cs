using ArchHarness.App.Agents;
using ArchHarness.App.Copilot;
using ArchHarness.App.Storage;
using ArchHarness.App.Workspace;
using System.Diagnostics;

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

    public OrchestratorRuntime(
        OrchestrationAgent orchestrationAgent,
        FrontendAgent frontendAgent,
        BuilderAgent builderAgent,
        ArchitectureAgent architectureAgent,
        IRunStore runStore,
        IArtefactStore artefactStore,
        ICopilotClient copilotClient,
        ICopilotSessionEventStream sessionEventStream)
    {
        _orchestrationAgent = orchestrationAgent;
        _frontendAgent = frontendAgent;
        _builderAgent = builderAgent;
        _architectureAgent = architectureAgent;
        _runStore = runStore;
        _artefactStore = artefactStore;
        _copilotClient = copilotClient;
        _sessionEventStream = sessionEventStream;
    }

    public async Task<RunArtefacts> RunAsync(
        RunRequest request,
        IProgress<RuntimeProgressEvent>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var adapter = WorkspaceAdapterFactory.Create(request.WorkspaceMode, request.WorkspacePath);
        var initGit = request.WorkspaceMode is "new-project" or "existing-git";
        await adapter.InitializeAsync(request.WorkspaceMode == "new-project" ? request.ProjectName : null, initGit, cancellationToken);

        var runDirectory = _runStore.CreateRunDirectory(adapter.RootPath);
        var runId = Path.GetFileName(runDirectory);
        using var sessionEventCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var sessionEventPump = Task.Run(async () => await PumpSessionEventsAsync(runDirectory, runId, sessionEventCts.Token), CancellationToken.None);

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
        progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, OrchestratorSource, "Run started"));
        var plan = await _orchestrationAgent.BuildExecutionPlanAsync(request, adapter.RootPath, cancellationToken);
        await _artefactStore.WriteExecutionPlanAsync(runDirectory, plan, cancellationToken);

        string frontendPlan = string.Empty;
        IReadOnlyList<string> filesTouched = Array.Empty<string>();
        ArchitectureReview review = new(Array.Empty<ArchitectureFinding>(), Array.Empty<string>());

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
            switch (step.Agent)
            {
                case "Frontend":
                    frontendPlan = await _frontendAgent.CreatePlanAsync(adapter, step.Objective, request.ModelOverrides, cancellationToken);
                    break;
                case "Builder":
                    filesTouched = await _builderAgent.ImplementAsync(adapter, step.Objective, request.ModelOverrides, cancellationToken: cancellationToken);
                    break;
                case "Architecture":
                    review = await _architectureAgent.ReviewAsync(step.Objective, await adapter.DiffAsync(cancellationToken), adapter.RootPath, filesTouched, request.ModelOverrides, cancellationToken);
                    break;
            }

                    completedStepIds.Add(step.Id);
                    pendingSteps.Remove(step.Id);
        }

        var iteration = 0;
        while (plan.IterationStrategy.ReviewRequired &&
               review.Findings.Any(f => f.Severity.Equals("high", StringComparison.OrdinalIgnoreCase)) &&
               iteration < plan.IterationStrategy.MaxIterations)
        {
            iteration++;
            progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, "architecture-loop", $"Review iteration {iteration}"));
            var remediationPrompt = await _orchestrationAgent.BuildRemediationPromptAsync(request, adapter.RootPath, review, iteration, cancellationToken);
            progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, "Architecture", "Enforcement prompt started", remediationPrompt));

            var latestDiff = await adapter.DiffAsync(cancellationToken);
            review = await _architectureAgent.ReviewAsync(remediationPrompt, latestDiff, adapter.RootPath, filesTouched, request.ModelOverrides, cancellationToken);

            // Refresh touched files snapshot after architecture enforcement pass.
            filesTouched = latestDiff
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => line.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        await _artefactStore.WriteArchitectureReviewAsync(runDirectory, review, cancellationToken);

        var buildResult = await RunBuildIfConfiguredAsync(request.BuildCommand, adapter.RootPath, cancellationToken);
        await _artefactStore.WriteBuildResultAsync(runDirectory, buildResult, cancellationToken);
        progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, "build", buildResult.Passed ? "Build passed" : "Build failed or not executed"));

        var buildCommandConfigured = !string.IsNullOrWhiteSpace(request.BuildCommand);
        var completed = await _orchestrationAgent.ValidateCompletionAsync(plan, review, buildResult.Passed, buildCommandConfigured, request.ModelOverrides, cancellationToken);
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

    private static async Task<BuildResult> RunBuildIfConfiguredAsync(string? buildCommand, string workingDirectory, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buildCommand))
        {
            return BuildResult.NotExecuted("Build command was not configured.");
        }

        var trimmed = buildCommand.Trim();
        if (!trimmed.StartsWith("dotnet build", StringComparison.OrdinalIgnoreCase))
        {
            return BuildResult.NotExecuted("Build command is not allow-listed. Only 'dotnet build ...' is supported.");
        }

        var args = trimmed.Length == "dotnet".Length ? string.Empty : trimmed["dotnet".Length..].TrimStart();
        var info = new ProcessStartInfo("dotnet", args)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = info };
        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var output = string.Join(Environment.NewLine, new[] { stdout, stderr }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return new BuildResult(Executed: true, Passed: process.ExitCode == 0, ExitCode: process.ExitCode, Output: Redaction.RedactSecrets(output));
    }

    private sealed record BuildResult(bool Executed, bool Passed, int? ExitCode, string Output)
    {
        public static BuildResult NotExecuted(string reason) => new(Executed: false, Passed: false, ExitCode: null, Output: reason);
    }
}
