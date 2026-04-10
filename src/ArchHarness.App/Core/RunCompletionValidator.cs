using ArchHarness.App.Agents;

namespace ArchHarness.App.Core;

/// <summary>
/// Validates whether a run satisfies its completion criteria.
/// </summary>
public interface IRunCompletionValidator
{
    /// <summary>
    /// Validates final run completion using the orchestration agent.
    /// </summary>
    Task<CompletionValidationResult> ValidateAsync(CompletionValidationRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Default implementation of <see cref="IRunCompletionValidator"/>.
/// </summary>
public sealed class RunCompletionValidator : IRunCompletionValidator
{
    private readonly OrchestrationAgent _orchestrationAgent;

    /// <summary>
    /// Initializes a new instance of the <see cref="RunCompletionValidator"/> class.
    /// </summary>
    public RunCompletionValidator(OrchestrationAgent orchestrationAgent)
    {
        this._orchestrationAgent = orchestrationAgent;
    }

    /// <inheritdoc />
    public Task<CompletionValidationResult> ValidateAsync(CompletionValidationRequest request, CancellationToken cancellationToken)
        => this._orchestrationAgent.ValidateCompletionAsync(
            request,
            this._orchestrationAgent.Id,
            this._orchestrationAgent.Role,
            cancellationToken);
}
