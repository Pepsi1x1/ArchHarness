using ArchHarness.App.Agents;
using ArchHarness.App.Constants;
using ArchHarness.App.Workspace;

namespace ArchHarness.App.Core;

/// <summary>
/// Coordinates executable verification, bounded remediation, and final completion validation.
/// </summary>
public interface IRunVerificationWorkflow
{
    /// <summary>
    /// Executes the verification workflow for a completed run.
    /// </summary>
    Task<VerificationWorkflowResult> RunAsync(
        RunVerificationRequest request,
        IWorkspaceAdapter adapter,
        IProgress<RuntimeProgressEvent>? progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// Request payload for the post-execution verification workflow.
/// </summary>
public sealed record RunVerificationRequest(
    RunRequest RunRequest,
    string RunDirectory,
    ExecutionPlan Plan,
    ArchitectureReview Review,
    SecurityReview SecurityReview,
    ClarificationSpec? Spec,
    BuildOutcome? LastBuildOutcome,
    IReadOnlyList<string> FilesTouched);

/// <summary>
/// Result of the post-execution verification workflow.
/// </summary>
public sealed record VerificationWorkflowResult(
    CompletionValidationResult ValidationResult,
    IReadOnlyList<string> FilesTouched,
    BuildOutcome? LastBuildOutcome);

/// <summary>
/// Default implementation of <see cref="IRunVerificationWorkflow"/>.
/// </summary>
public sealed class RunVerificationWorkflow : IRunVerificationWorkflow
{
    private const int MAX_VERIFICATION_ATTEMPTS = 3;

    private readonly IVerificationCommandRunner _verificationCommandRunner;
    private readonly IRunCompletionValidator _completionValidator;
    private readonly FrontendDeveloperAgent _frontendDeveloper;
    private readonly BackendDeveloperAgent _backendDeveloper;
    private readonly RuntimeStateAccessors _stateAccessors;

    /// <summary>
    /// Initializes a new instance of the <see cref="RunVerificationWorkflow"/> class.
    /// </summary>
    public RunVerificationWorkflow(
        IVerificationCommandRunner verificationCommandRunner,
        IRunCompletionValidator completionValidator,
        FrontendDeveloperAgent frontendDeveloper,
        BackendDeveloperAgent backendDeveloper,
        RuntimeStateAccessors stateAccessors)
    {
        this._verificationCommandRunner = verificationCommandRunner;
        this._completionValidator = completionValidator;
        this._frontendDeveloper = frontendDeveloper;
        this._backendDeveloper = backendDeveloper;
        this._stateAccessors = stateAccessors;
    }

    /// <inheritdoc />
    public async Task<VerificationWorkflowResult> RunAsync(
        RunVerificationRequest request,
        IWorkspaceAdapter adapter,
        IProgress<RuntimeProgressEvent>? progress,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<VerificationCommand> commands = ResolveVerificationCommands(request);
        IReadOnlyList<string> filesTouched = request.FilesTouched;
        BuildOutcome? lastBuildOutcome = request.LastBuildOutcome;
        List<VerificationAttempt> attempts = new List<VerificationAttempt>();
        string? remediationPrompt = null;
        CompletionValidationResult validationResult = new CompletionValidationResult(false, Array.Empty<CriterionResult>());
        int maxAttempts = commands.Count == 0 ? 1 : MAX_VERIFICATION_ATTEMPTS;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            IReadOnlyList<VerificationEvidence> evidence = await this._verificationCommandRunner.RunAsync(adapter.RootPath, commands, progress, cancellationToken).ConfigureAwait(false);
            lastBuildOutcome = UpdateBuildOutcome(lastBuildOutcome, evidence);
            validationResult = await this._completionValidator.ValidateAsync(
                new CompletionValidationRequest(
                    request.Plan,
                    request.Review,
                    request.SecurityReview,
                    request.RunRequest.ModelOverrides,
                    request.Spec,
                    lastBuildOutcome,
                    evidence,
                    filesTouched,
                    request.RunRequest.WorkspacePath,
                    request.RunDirectory,
                    request.RunRequest.Workflow),
                cancellationToken).ConfigureAwait(false);

            attempts.Add(new VerificationAttempt(
                attempt,
                validationResult.Passed,
                validationResult.Summary,
                evidence,
                remediationPrompt,
                DateTimeOffset.UtcNow));

            validationResult = validationResult with
            {
                Evidence = evidence,
                Attempts = attempts.ToArray()
            };

            if (validationResult.Passed || attempt >= maxAttempts)
            {
                break;
            }

            remediationPrompt = BuildRemediationPrompt(validationResult, attempt + 1);
            IReadOnlyList<string> touchedFiles = await this.ExecuteRemediationAsync(request, adapter, remediationPrompt, progress, cancellationToken).ConfigureAwait(false);
            filesTouched = filesTouched
                .Concat(touchedFiles)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return new VerificationWorkflowResult(validationResult, filesTouched, lastBuildOutcome);
    }

    private static IReadOnlyList<VerificationCommand> ResolveVerificationCommands(RunVerificationRequest request)
    {
        List<VerificationCommand> commands = request.Spec?.VerificationCommands?.ToList() ?? new List<VerificationCommand>();
        if (string.IsNullOrWhiteSpace(request.RunRequest.BuildCommand))
        {
            return commands;
        }

        bool requiresBuildVerification = request.Plan.CompletionCriteria.Any(CompletionCriteriaSupport.IsBuildCriterion)
            || request.Spec?.AcceptanceCriteria.Any(CompletionCriteriaSupport.IsBuildCriterion) == true;
        bool hasBuildVerificationCommand = commands.Any(command => string.Equals(command.EvidenceType, "build", StringComparison.OrdinalIgnoreCase)
            || CompletionCriteriaSupport.IsBuildCriterion(command.Criterion ?? command.Name));

        if (requiresBuildVerification && !hasBuildVerificationCommand)
        {
            commands.Add(new VerificationCommand(
                "Build validation",
                request.RunRequest.BuildCommand!,
                EvidenceType: "build",
                Criterion: "Build passes",
                Required: true));
        }

        return commands;
    }

    private async Task<IReadOnlyList<string>> ExecuteRemediationAsync(
        RunVerificationRequest request,
        IWorkspaceAdapter adapter,
        string remediationPrompt,
        IProgress<RuntimeProgressEvent>? progress,
        CancellationToken cancellationToken)
    {
        List<string> touchedFiles = new List<string>();
        foreach (AgentExecutionContext agent in this.ResolveRemediationAgents(request.Plan))
        {
            progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, agent.AgentRole, "Verification remediation started", remediationPrompt));
            AgentExecutionContext? previousContext = this._stateAccessors.AgentExecutionContext.Current;
            this._stateAccessors.AgentExecutionContext.SetCurrent(agent);
            try
            {
                IReadOnlyList<string> files = string.Equals(agent.AgentRole, this._frontendDeveloper.Role, StringComparison.OrdinalIgnoreCase)
                    ? await this._frontendDeveloper.ImplementAsync(adapter, remediationPrompt, request.RunRequest.ModelOverrides, agent.AgentId, agent.AgentRole, cancellationToken).ConfigureAwait(false)
                    : await this._backendDeveloper.ImplementAsync(adapter, remediationPrompt, request.RunRequest.ModelOverrides, null, agent.AgentId, agent.AgentRole, cancellationToken).ConfigureAwait(false);
                touchedFiles.AddRange(files);
            }
            finally
            {
                this._stateAccessors.AgentExecutionContext.SetCurrent(previousContext);
            }
        }

        return touchedFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private IEnumerable<AgentExecutionContext> ResolveRemediationAgents(ExecutionPlan plan)
    {
        HashSet<string> emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string stepAgent in plan.Steps.Select(step => step.Agent))
        {
            if (string.Equals(stepAgent, AgentNames.FRONTEND_DEVELOPER, StringComparison.OrdinalIgnoreCase)
                && emitted.Add(this._frontendDeveloper.Role))
            {
                yield return new AgentExecutionContext(this._frontendDeveloper.Id, this._frontendDeveloper.Role);
            }
            else if (string.Equals(stepAgent, AgentNames.BACKEND_DEVELOPER, StringComparison.OrdinalIgnoreCase)
                && emitted.Add(this._backendDeveloper.Role))
            {
                yield return new AgentExecutionContext(this._backendDeveloper.Id, this._backendDeveloper.Role);
            }
        }

        if (emitted.Count == 0)
        {
            yield return new AgentExecutionContext(this._backendDeveloper.Id, this._backendDeveloper.Role);
        }
    }

    private static BuildOutcome? UpdateBuildOutcome(BuildOutcome? fallbackBuildOutcome, IReadOnlyList<VerificationEvidence> evidence)
    {
        VerificationEvidence? buildEvidence = CompletionCriteriaSupport.FindLatestBuildEvidence(evidence);
        if (buildEvidence is null)
        {
            return fallbackBuildOutcome;
        }

        return new BuildOutcome(
            buildEvidence.Passed,
            buildEvidence.Summary,
            fallbackBuildOutcome?.StepId ?? 0,
            buildEvidence.TimestampUtc ?? DateTimeOffset.UtcNow,
            buildEvidence.Command,
            buildEvidence.ExitCode,
            buildEvidence.Output,
            buildEvidence.ErrorOutput);
    }

    private static string BuildRemediationPrompt(CompletionValidationResult validationResult, int nextAttempt)
    {
        string failedCriteria = validationResult.CriterionResults.Count == 0
            ? "- No explicit failed criteria were reported."
            : string.Join(Environment.NewLine, validationResult.CriterionResults
                .Where(result => !result.Passed)
                .Select(result => $"- {result.Criterion}: {result.Evidence}"));
        string verifierGaps = validationResult.Assessment is not { Gaps.Count: > 0 }
            ? string.Empty
            : $"{Environment.NewLine}VerifierGaps:{Environment.NewLine}{string.Join(Environment.NewLine, validationResult.Assessment.Gaps.Select(gap => $"- {gap}"))}";

        return $"""
            Verification remediation attempt {nextAttempt}.
            Fix the workspace so the following completion criteria pass on the next verification run.

            FailedCriteria:
            {failedCriteria}
            {verifierGaps}

            Constraints:
            - Make the smallest code changes needed to satisfy the failed criteria.
            - Add or update tests when required by the failing verification.
            - Do not change the verification commands themselves unless the codebase requires a matching command target/path fix.
            - Do not stop at analysis; implement the necessary file changes directly in the workspace.
            """;
    }
}
