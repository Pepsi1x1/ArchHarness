using ArchHarness.App.Agents;
using ArchHarness.App.Workspace;

namespace ArchHarness.App.Core;

/// <summary>
/// Result of build execution and completion validation.
/// </summary>
/// <param name="BuildResult">The build execution result.</param>
/// <param name="Completed">Whether the run is considered complete.</param>
public sealed record BuildValidationResult(BuildResult BuildResult, bool Completed);

/// <summary>
/// Executes the final build and validates run completion.
/// </summary>
public sealed class BuildValidator
{
    private readonly IBuildRunner _buildRunner;
    private readonly OrchestrationAgent _orchestrationAgent;
    private readonly RunEventLogger _eventLogger;
    private readonly RunArtifactWriter _artifactWriter;

    /// <summary>
    /// Initializes a new instance of the <see cref="BuildValidator"/> class.
    /// </summary>
    /// <param name="buildRunner">Runner that executes build commands.</param>
    /// <param name="orchestrationAgent">Agent used for completion validation.</param>
    /// <param name="eventLogger">Logger for run events.</param>
    /// <param name="artifactWriter">Writer for run artifacts.</param>
    public BuildValidator(
        IBuildRunner buildRunner,
        OrchestrationAgent orchestrationAgent,
        RunEventLogger eventLogger,
        RunArtifactWriter artifactWriter)
    {
        _buildRunner = buildRunner;
        _orchestrationAgent = orchestrationAgent;
        _eventLogger = eventLogger;
        _artifactWriter = artifactWriter;
    }

    /// <summary>
    /// Runs the final build, persists results, and validates run completion.
    /// </summary>
    /// <param name="plan">The execution plan used for validation criteria.</param>
    /// <param name="review">The architecture review used for validation.</param>
    /// <param name="adapter">Workspace adapter providing the root path.</param>
    /// <param name="request">The originating run request.</param>
    /// <param name="runId">Unique identifier for the current run.</param>
    /// <param name="runDirectory">Directory where run artefacts are stored.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Token to signal cancellation.</param>
    /// <returns>The build result and whether the run completed successfully.</returns>
    public async Task<BuildValidationResult> ExecuteAndValidateAsync(
        ExecutionPlan plan,
        ArchitectureReview review,
        IWorkspaceAdapter adapter,
        RunRequest request,
        string runId,
        string runDirectory,
        IProgress<RuntimeProgressEvent>? progress,
        CancellationToken cancellationToken)
    {
        BuildCommandSelection finalBuildSelection = BuildCommandInference.Select(
            adapter.RootPath,
            request.BuildCommand,
            request.WorkspaceMode,
            request.ProjectName);

        await _eventLogger.AppendEventAsync(runDirectory, new
        {
            runId,
            source = "build-selection",
            message = "Final build command selection",
            buildCommand = finalBuildSelection.Command,
            inferred = finalBuildSelection.Inferred,
            reason = finalBuildSelection.Reason
        }, cancellationToken);

        BuildResult buildResult = await _buildRunner.RunAsync(finalBuildSelection.Command, adapter.RootPath, cancellationToken);
        await _artifactWriter.WriteBuildResultAsync(runDirectory, buildResult, cancellationToken);
        progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, "build", buildResult.Passed ? "Build passed" : "Build failed or not executed"));

        bool buildCommandConfigured = !string.IsNullOrWhiteSpace(request.BuildCommand);
        bool completed = await _orchestrationAgent.ValidateCompletionAsync(
            new CompletionValidationRequest(
                Plan: plan,
                Review: review,
                BuildPassed: buildResult.Passed,
                BuildCommandConfigured: buildCommandConfigured,
                ModelOverrides: request.ModelOverrides),
            _orchestrationAgent.Id,
            _orchestrationAgent.Role,
            cancellationToken);

        return new BuildValidationResult(buildResult, completed);
    }
}
