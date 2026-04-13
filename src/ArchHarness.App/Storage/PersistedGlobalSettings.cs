namespace ArchHarness.App.Storage;

/// <summary>
/// Represents the persisted global settings used by all hosts and future runs.
/// </summary>
public sealed record PersistedGlobalSettings(
    string ConversationModel,
    string OrchestrationModel,
    string PlanningModel,
    string? PlanningReasoningEffort,
    string FrontendDeveloperModel,
    string BackendDeveloperModel,
    string BuildModel,
    string CodingStyleModel,
    string SecurityModel,
    string ArchitectureModel,
    string WikiDocModel,
    string? WikiDocReasoningEffort,
    int WikiDocParallelism,
    string DefaultPermissionHandlerMode,
    bool DefaultArchitectureReviewMode,
    string? DefaultArchitectureReviewPrompt,
    DateTimeOffset UpdatedAtUtc);
