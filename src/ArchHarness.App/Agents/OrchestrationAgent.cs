using ArchHarness.App.Core;
using ArchHarness.App.Copilot;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Agents;

public sealed class OrchestrationAgent : AgentBase
{
    public OrchestrationAgent(ICopilotClient copilotClient, IOptions<AgentsOptions> options)
        : base(copilotClient, options.Value.Orchestration.Model) { }

    public async Task<ExecutionPlan> BuildExecutionPlanAsync(RunRequest request, CancellationToken cancellationToken = default)
    {
        _ = await CopilotClient.CompleteAsync(Model, $"Create execution plan for: {request.TaskPrompt}", cancellationToken);
        var steps = new List<ExecutionPlanStep>
        {
            new(1, "Frontend", "Define UI architecture"),
            new(2, "Builder", "Implement requested changes"),
            new(3, "Architecture", "Review for SOLID/DRY and separation of concerns")
        };

        return new ExecutionPlan(
            steps,
            new IterationStrategy(MaxIterations: 2, ReviewRequired: true),
            new List<string>
            {
                "No high severity architecture findings",
                "Build passes (if command configured)"
            }
        );
    }

    public async Task<bool> ValidateCompletionAsync(ArchitectureReview review, CancellationToken cancellationToken = default)
    {
        _ = await CopilotClient.CompleteAsync(Model, "Validate completion", cancellationToken);
        return review.Findings.All(f => !string.Equals(f.Severity, "high", StringComparison.OrdinalIgnoreCase));
    }
}
