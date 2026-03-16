namespace ArchHarness.App.Storage;

/// <summary>
/// Describes the mutable global settings values that can be persisted by a host.
/// </summary>
public sealed record UpdatePersistedGlobalSettings(
    string ConversationModel,
    string OrchestrationModel,
    string FrontendDeveloperModel,
    string BackendDeveloperModel,
    string BuildModel,
    string CodingStyleModel,
    string SecurityModel,
    string ArchitectureModel,
    string DefaultPermissionHandlerMode,
    bool DefaultArchitectureReviewMode,
    string? DefaultArchitectureReviewPrompt)
{
    /// <summary>
    /// Returns all configured model identifiers contained in the update.
    /// </summary>
    public IEnumerable<string> GetConfiguredModels()
    {
        yield return this.ConversationModel;
        yield return this.OrchestrationModel;
        yield return this.FrontendDeveloperModel;
        yield return this.BackendDeveloperModel;
        yield return this.BuildModel;
        yield return this.CodingStyleModel;
        yield return this.SecurityModel;
        yield return this.ArchitectureModel;
    }
}