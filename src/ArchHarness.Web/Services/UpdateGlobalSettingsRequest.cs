namespace ArchHarness.Web.Services;

/// <summary>
/// Represents a full replacement of the persisted global settings used by the web shell.
/// </summary>
public sealed record UpdateGlobalSettingsRequest(
    AgentModelSettingsRequest AgentModels,
    DefaultSettingsRequest Defaults);

/// <summary>
/// Structured per-agent model selections for the settings UI.
/// </summary>
public sealed record AgentModelSettingsRequest(
    string Conversation,
    string Orchestration,
    string FrontendDeveloper,
    string BackendDeveloper,
    string Build,
    string CodingStyle,
    string Security,
    string Architecture);

/// <summary>
/// Structured global defaults surfaced to the shell.
/// </summary>
public sealed record DefaultSettingsRequest(
    string PermissionHandlerMode,
    bool ArchitectureReviewMode,
    string? ArchitectureReviewPrompt);