using System.Text.Json;
using ArchHarness.App.Core;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Storage;

/// <summary>
/// Persists global ArchHarness settings in a user-scoped JSON file.
/// </summary>
public sealed class FileSystemGlobalSettingsCatalog : IGlobalSettingsCatalog
{
    private readonly object _sync = new object();
    private readonly string _storageFilePath;
    private readonly AgentsOptions _agentsOptions;
    private readonly CopilotOptions _copilotOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemGlobalSettingsCatalog"/> class.
    /// </summary>
    public FileSystemGlobalSettingsCatalog(IOptions<AgentsOptions> agentsOptions, IOptions<CopilotOptions> copilotOptions)
        : this(GetDefaultStorageFilePath(), agentsOptions.Value, copilotOptions.Value)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemGlobalSettingsCatalog"/> class using explicit defaults and storage path.
    /// </summary>
    public FileSystemGlobalSettingsCatalog(string storageFilePath, AgentsOptions agentsOptions, CopilotOptions copilotOptions)
    {
        this._storageFilePath = FileSystemStorageHelper.NormalizePath(storageFilePath);
        this._agentsOptions = agentsOptions;
        this._copilotOptions = copilotOptions;
    }

    /// <inheritdoc />
    public PersistedGlobalSettings GetSettings()
    {
        lock (this._sync)
        {
            PersistedGlobalSettingsDocument? persisted = this.LoadPersistedDocument();
            return persisted is null ? this.BuildDefaultSettings() : this.MapFromPersisted(persisted);
        }
    }

    /// <inheritdoc />
    public PersistedGlobalSettings UpdateSettings(UpdatePersistedGlobalSettings update)
    {
        lock (this._sync)
        {
            PersistedGlobalSettings settings = new PersistedGlobalSettings(
                NormalizeModel(update.ConversationModel, this._copilotOptions.ConversationModel),
                NormalizeModel(update.OrchestrationModel, this._agentsOptions.Orchestration.Model),
                NormalizeModel(update.PlanningModel, this._agentsOptions.Planning.Model),
                NormalizeReasoningEffort(update.PlanningReasoningEffort),
                NormalizeModel(update.FrontendDeveloperModel, this._agentsOptions.FrontendDeveloper.Model),
                NormalizeModel(update.BackendDeveloperModel, this._agentsOptions.BackendDeveloper.Model),
                NormalizeModel(update.BuildModel, this._agentsOptions.Build.Model),
                NormalizeModel(update.CodingStyleModel, this._agentsOptions.CodingStyle.Model),
                NormalizeModel(update.SecurityModel, this._agentsOptions.Security.Model),
                NormalizeModel(update.ArchitectureModel, this._agentsOptions.Architecture.Model),
                PermissionHandlerModes.Normalize(update.DefaultPermissionHandlerMode),
                update.DefaultArchitectureReviewMode,
                string.IsNullOrWhiteSpace(update.DefaultArchitectureReviewPrompt) ? null : update.DefaultArchitectureReviewPrompt.Trim(),
                DateTimeOffset.UtcNow);

            this.SaveSettings(settings);
            return settings;
        }
    }

    private PersistedGlobalSettingsDocument? LoadPersistedDocument()
    {
        if (!File.Exists(this._storageFilePath))
        {
            return null;
        }

        try
        {
            string json = File.ReadAllText(this._storageFilePath);
            return JsonSerializer.Deserialize<PersistedGlobalSettingsDocument>(json, JsonDefaults.WEB_INDENTED);
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private PersistedGlobalSettings BuildDefaultSettings()
        => new PersistedGlobalSettings(
            NormalizeModel(this._copilotOptions.ConversationModel, "gpt-5-mini"),
            NormalizeModel(this._agentsOptions.Orchestration.Model, "claude-sonnet-4.6"),
            NormalizeModel(this._agentsOptions.Planning.Model, "gpt-5.4"),
            NormalizeReasoningEffort(this._agentsOptions.Planning.ReasoningEffort),
            NormalizeModel(this._agentsOptions.FrontendDeveloper.Model, "claude-sonnet-4.6"),
            NormalizeModel(this._agentsOptions.BackendDeveloper.Model, "gpt-5.3-codex"),
            NormalizeModel(this._agentsOptions.Build.Model, "gpt-4.1"),
            NormalizeModel(this._agentsOptions.CodingStyle.Model, "claude-opus-4.6"),
            NormalizeModel(this._agentsOptions.Security.Model, "claude-opus-4.6"),
            NormalizeModel(this._agentsOptions.Architecture.Model, "claude-opus-4.6"),
            PermissionHandlerModes.APPROVE_ALL,
            this._agentsOptions.Architecture.ArchitectureLoopMode,
            string.IsNullOrWhiteSpace(this._agentsOptions.Architecture.ArchitectureLoopPrompt)
                ? null
                : this._agentsOptions.Architecture.ArchitectureLoopPrompt.Trim(),
            DateTimeOffset.UtcNow);

    private void SaveSettings(PersistedGlobalSettings settings)
    {
        PersistedGlobalSettingsDocument persisted = MapToPersisted(settings);
        FileSystemStorageHelper.WriteJsonFile(this._storageFilePath, persisted, JsonDefaults.WEB_INDENTED);
    }

    private PersistedGlobalSettings MapFromPersisted(PersistedGlobalSettingsDocument persisted)
        => new PersistedGlobalSettings(
            NormalizeModel(persisted.ConversationModel, this._copilotOptions.ConversationModel),
            NormalizeModel(persisted.OrchestrationModel, this._agentsOptions.Orchestration.Model),
            NormalizeModel(persisted.PlanningModel, this._agentsOptions.Planning.Model),
            NormalizeReasoningEffort(persisted.PlanningReasoningEffort),
            NormalizeModel(persisted.FrontendDeveloperModel, this._agentsOptions.FrontendDeveloper.Model),
            NormalizeModel(persisted.BackendDeveloperModel, this._agentsOptions.BackendDeveloper.Model),
            NormalizeModel(persisted.BuildModel, this._agentsOptions.Build.Model),
            NormalizeModel(persisted.CodingStyleModel, this._agentsOptions.CodingStyle.Model),
            NormalizeModel(persisted.SecurityModel, this._agentsOptions.Security.Model),
            NormalizeModel(persisted.ArchitectureModel, this._agentsOptions.Architecture.Model),
            PermissionHandlerModes.Normalize(persisted.DefaultPermissionHandlerMode),
            persisted.DefaultArchitectureReviewMode,
            string.IsNullOrWhiteSpace(persisted.DefaultArchitectureReviewPrompt) ? null : persisted.DefaultArchitectureReviewPrompt.Trim(),
            persisted.UpdatedAtUtc);

    private static PersistedGlobalSettingsDocument MapToPersisted(PersistedGlobalSettings settings)
        => new PersistedGlobalSettingsDocument
        {
            ConversationModel = settings.ConversationModel,
            OrchestrationModel = settings.OrchestrationModel,
            PlanningModel = settings.PlanningModel,
            PlanningReasoningEffort = settings.PlanningReasoningEffort,
            FrontendDeveloperModel = settings.FrontendDeveloperModel,
            BackendDeveloperModel = settings.BackendDeveloperModel,
            BuildModel = settings.BuildModel,
            CodingStyleModel = settings.CodingStyleModel,
            SecurityModel = settings.SecurityModel,
            ArchitectureModel = settings.ArchitectureModel,
            DefaultPermissionHandlerMode = settings.DefaultPermissionHandlerMode,
            DefaultArchitectureReviewMode = settings.DefaultArchitectureReviewMode,
            DefaultArchitectureReviewPrompt = settings.DefaultArchitectureReviewPrompt,
            UpdatedAtUtc = settings.UpdatedAtUtc
        };

    private static string GetDefaultStorageFilePath()
        => FileSystemStorageHelper.GetAppDataFilePath("settings.json");

    private static string NormalizeModel(string? model, string fallback)
        => string.IsNullOrWhiteSpace(model) ? fallback : model.Trim();

    private static string? NormalizeReasoningEffort(string? reasoningEffort)
    {
        if (string.IsNullOrWhiteSpace(reasoningEffort))
        {
            return null;
        }

        string normalized = reasoningEffort.Trim().ToLowerInvariant();
        return normalized is "low" or "medium" or "high" or "xhigh"
            ? normalized
            : null;
    }

    private sealed class PersistedGlobalSettingsDocument
    {
        public string? ConversationModel { get; init; }

        public string? OrchestrationModel { get; init; }

        public string? PlanningModel { get; init; }

        public string? PlanningReasoningEffort { get; init; }

        public string? FrontendDeveloperModel { get; init; }

        public string? BackendDeveloperModel { get; init; }

        public string? BuildModel { get; init; }

        public string? CodingStyleModel { get; init; }

        public string? SecurityModel { get; init; }

        public string? ArchitectureModel { get; init; }

        public string? DefaultPermissionHandlerMode { get; init; }

        public bool DefaultArchitectureReviewMode { get; init; }

        public string? DefaultArchitectureReviewPrompt { get; init; }

        public DateTimeOffset UpdatedAtUtc { get; init; }
    }
}
