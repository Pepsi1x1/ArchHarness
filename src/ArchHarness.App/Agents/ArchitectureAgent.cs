using ArchHarness.App.Copilot;
using ArchHarness.App.Core;

namespace ArchHarness.App.Agents;

public sealed class ArchitectureAgent
{
    private readonly CopilotClient _copilotClient;
    private readonly string _model;

    public ArchitectureAgent(CopilotClient copilotClient, string model)
    {
        _copilotClient = copilotClient;
        _model = model;
    }

    public async Task<ArchitectureReview> ReviewAsync(string diff, IReadOnlyList<string> filesTouched, CancellationToken cancellationToken = default)
    {
        _ = await _copilotClient.CompleteAsync(_model, "Run architecture review", cancellationToken);

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
