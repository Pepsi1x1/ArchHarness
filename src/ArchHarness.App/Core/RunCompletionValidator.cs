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
    Task<bool> ValidateAsync(
        ExecutionPlan plan,
        ArchitectureReview review,
        SecurityReview securityReview,
        IDictionary<string, string>? modelOverrides,
        CancellationToken cancellationToken);
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
    public Task<bool> ValidateAsync(
        ExecutionPlan plan,
        ArchitectureReview review,
        SecurityReview securityReview,
        IDictionary<string, string>? modelOverrides,
        CancellationToken cancellationToken)
        => this._orchestrationAgent.ValidateCompletionAsync(
            new CompletionValidationRequest(plan, review, securityReview, modelOverrides),
            this._orchestrationAgent.Id,
            this._orchestrationAgent.Role,
            cancellationToken);
}
