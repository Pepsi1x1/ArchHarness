namespace ArchHarness.Desktop.ViewModels;

public sealed class AgentItemViewModel : ViewModelBase
{
    public AgentItemViewModel(string agentId, string role)
    {
        this.AgentId = agentId;
        this.Role = role;
    }

    public string AgentId { get; }

    public string Role { get; }

    public string DisplayName => $"{this.Role} ({this.AgentId[..Math.Min(8, this.AgentId.Length)]})";
}