using ArchHarness.App.Copilot;

namespace ArchHarness.App.Agents;

public sealed class FrontendAgent
{
    private readonly CopilotClient _copilotClient;
    private readonly string _model;

    public FrontendAgent(CopilotClient copilotClient, string model)
    {
        _copilotClient = copilotClient;
        _model = model;
    }

    public Task<string> CreatePlanAsync(string taskPrompt, CancellationToken cancellationToken = default)
        => _copilotClient.CompleteAsync(_model, $"Generate frontend plan: {taskPrompt}", cancellationToken);
}
