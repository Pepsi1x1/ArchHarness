using System.Text.Json;
using ArchHarness.App.Core;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Storage;

/// <summary>
/// Persists global ArchHarness settings in a user-scoped JSON file.
/// </summary>
public sealed class FileSystemGlobalSettingsCatalog : IGlobalSettingsCatalog
{
    private static readonly JsonSerializerOptions SERIALIZER_OPTIONS = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

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
        this._storageFilePath = storageFilePath;
        this._agentsOptions = agentsOptions;
        this._copilotOptions = copilotOptions;
    }

    /// <inheritdoc />
    public PersistedGlobalSettings GetSettings()
    {
        lock (this._sync)
        {
            return this.LoadSettings() ?? this.BuildDefaultSettings();
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

            SaveSettings(settings);
            return settings;
        }
    }

    private PersistedGlobalSettings? LoadSettings()
    {
        if (!File.Exists(this._storageFilePath))
        {
            return null;
        }

        try
        {
            string json = File.ReadAllText(this._storageFilePath);
            return JsonSerializer.Deserialize<PersistedGlobalSettings>(json, SERIALIZER_OPTIONS);
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
        string? directory = Path.GetDirectoryName(this._storageFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(settings, SERIALIZER_OPTIONS);
        File.WriteAllText(this._storageFilePath, json);
    }

    private static string GetDefaultStorageFilePath()
    {
        string appDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appDataRoot, "ArchHarness", "settings.json");
    }

    private static string NormalizeModel(string? model, string fallback)
        => string.IsNullOrWhiteSpace(model) ? fallback : model.Trim();
}