namespace ArchHarness.App.Core;

/// <summary>
/// Mutable draft state for the interactive setup form, populated during user input and
/// finalized into a RunRequest.
/// </summary>
internal sealed class SetupDraft
{
    private const string EXISTING_FOLDER_MODE = "existing-folder";

    /// <summary>Gets or sets the task prompt.</summary>
    public string TaskPrompt { get; set; } = string.Empty;

    /// <summary>Gets or sets the workspace path.</summary>
    public string WorkspacePath { get; set; } = string.Empty;

    /// <summary>Gets or sets the workspace mode.</summary>
    public string WorkspaceMode { get; set; } = EXISTING_FOLDER_MODE;

    /// <summary>Gets or sets the optional project name.</summary>
    public string? ProjectName { get; set; }

    /// <summary>Gets or sets the optional model overrides text.</summary>
    public string? ModelOverrides { get; set; }

    /// <summary>Gets or sets the optional build command.</summary>
    public string? BuildCommand { get; set; }

    /// <summary>Gets or sets whether architecture loop mode is enabled.</summary>
    public bool ArchitectureLoopMode { get; set; } = false;

    /// <summary>Gets or sets the optional architecture loop prompt.</summary>
    public string? ArchitectureLoopPrompt { get; set; }
}
