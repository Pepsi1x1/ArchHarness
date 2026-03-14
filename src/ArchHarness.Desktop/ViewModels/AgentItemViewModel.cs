namespace ArchHarness.Desktop.ViewModels;

/// <summary>
/// Represents a streaming agent for display in the desktop agent selector.
/// </summary>
public sealed class AgentItemViewModel : ViewModelBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AgentItemViewModel"/> class.
    /// </summary>
    /// <param name="agentId">The unique agent identifier.</param>
    /// <param name="role">The agent role (e.g., "backend-developer", "architecture").</param>
    public AgentItemViewModel(string agentId, string role)
    {
        this.AgentId = agentId;
        this.Role = role;
    }

    /// <summary>Gets the unique agent identifier.</summary>
    public string AgentId { get; }

    /// <summary>Gets the agent role.</summary>
    public string Role { get; }

    /// <summary>Gets the formatted display name combining role and truncated agent ID.</summary>
    public string DisplayName => $"{this.Role} ({this.AgentId[..Math.Min(8, this.AgentId.Length)]})";
}