using ArchHarness.App.Copilot;

namespace ArchHarness.App.Agents;

public abstract class AgentBase
{
    protected readonly ICopilotClient CopilotClient;
    public string Model { get; }

    protected AgentBase(ICopilotClient copilotClient, string model)
    {
        CopilotClient = copilotClient;
        Model = model;
    }
}
