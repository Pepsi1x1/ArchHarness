using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Agents;

public sealed class ArchitectureAgent : AgentBase
{
    public ArchitectureAgent(ICopilotClient copilotClient, IOptions<AgentsOptions> options)
        : base(copilotClient, options.Value.Architecture.Model) { }

    public async Task<ArchitectureReview> ReviewAsync(string diff, IReadOnlyList<string> filesTouched, CancellationToken cancellationToken = default)
    {
        _ = await CopilotClient.CompleteAsync(Model, "Run architecture review", cancellationToken);

        if (diff.Contains("TODO", StringComparison.OrdinalIgnoreCase))
        {
            var finding = new ArchitectureFinding(
                "high",
                "DRY/SOLID",
                filesTouched.FirstOrDefault(),
                "TODO",
                "TODO marker found in implementation."
            );
            return new ArchitectureReview(new[] { finding }, new[] { "Remove TODO markers and complete implementation details." });
        }

        return new ArchitectureReview(Array.Empty<ArchitectureFinding>(), Array.Empty<string>());
    }
}
