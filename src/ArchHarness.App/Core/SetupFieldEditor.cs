namespace ArchHarness.App.Core;

/// <summary>
/// Handles field editing, validation, and draft-to-request construction for the interactive setup form.
/// </summary>
internal static class SetupFieldEditor
{
    private const string NONE_TEXT = "(none)";
    private const string EXISTING_FOLDER_MODE = "existing-folder";
    private const string NEW_PROJECT_MODE = "new-project";
    private const string WORKSPACE_PATH_FIELD = "WorkspacePath";
    private const string DEFAULT_ARCH_LOOP_TASK_PROMPT = "Run architecture and style review loop for the existing workspace and apply required remediation.";

    private static readonly Dictionary<string, Action<SetupDraft, string>> FieldSetters = new Dictionary<string, Action<SetupDraft, string>>(StringComparer.Ordinal)
    {
        ["TaskPrompt"] = (draft, value) => draft.TaskPrompt = value,
        ["WorkspacePath"] = (draft, value) => draft.WorkspacePath = value,
        ["ProjectName"] = (draft, value) => draft.ProjectName = value,
        ["Workflow"] = (draft, value) => draft.ModelOverrides = value,
        ["ModelOverrides"] = (draft, value) => draft.ModelOverrides = value,
        ["BuildCommand"] = (draft, value) => draft.BuildCommand = value,
        ["ArchitectureLoopPrompt"] = (draft, value) => draft.ArchitectureLoopPrompt = string.IsNullOrWhiteSpace(value) ? null : value
    };

    /// <summary>
    /// Applies an edit to the draft for the given field. Toggle fields cycle their value;
    /// text fields prompt the user for input via the console.
    /// </summary>
    /// <param name="fieldId">The field identifier to edit.</param>
    /// <param name="draft">The draft to update.</param>
    public static void ApplyEdit(string fieldId, SetupDraft draft)
    {
        if (fieldId == "WorkspaceMode" || fieldId == "ArchitectureLoopMode")
        {
            if (fieldId == "WorkspaceMode")
            {
                draft.WorkspaceMode = SetupNavigator.NextMode(draft.WorkspaceMode, 1);
            }
            else
            {
                draft.ArchitectureLoopMode = !draft.ArchitectureLoopMode;
            }

            return;
        }

        try
        {
            Console.CursorVisible = true;
            Console.SetCursorPosition(0, Console.CursorTop + 2);
            Console.Write($"Edit {fieldId}> ");
            string? value = fieldId == WORKSPACE_PATH_FIELD
                ? PathInputHandler.ReadPathWithTabCompletion(draft.WorkspacePath)
                : Console.ReadLine();
            if (value is null)
            {
                return;
            }

            if (FieldSetters.TryGetValue(fieldId, out Action<SetupDraft, string>? setter))
            {
                setter(draft, value);
            }
        }
        finally
        {
            Console.CursorVisible = false;
        }
    }

    /// <summary>
    /// Returns the field ID of the first failing required field, or null if all pass.
    /// </summary>
    /// <param name="draft">The draft to validate.</param>
    /// <returns>The failing field ID, or null.</returns>
    public static string? ValidateRequiredFields(SetupDraft draft)
    {
        if (draft.ArchitectureLoopMode)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(draft.TaskPrompt) ? "TaskPrompt" : null;
    }

    /// <summary>
    /// Builds the list of setup fields from the current draft state.
    /// </summary>
    /// <param name="draft">The current draft.</param>
    /// <returns>The ordered field list for rendering.</returns>
    public static List<SetupField> BuildFields(SetupDraft draft)
    {
        List<SetupField> fields = new List<SetupField>
        {
            new SetupField("TaskPrompt", "Task", draft.TaskPrompt),
            new SetupField("WorkspacePath", "Workspace Path", draft.WorkspacePath),
            new SetupField("WorkspaceMode", "Workspace Mode", draft.WorkspaceMode),
            new SetupField("ModelOverrides", "Model Overrides", string.IsNullOrWhiteSpace(draft.ModelOverrides) ? NONE_TEXT : draft.ModelOverrides),
            new SetupField("BuildCommand", "Build Command", string.IsNullOrWhiteSpace(draft.BuildCommand) ? NONE_TEXT : draft.BuildCommand)
        };

        if (string.Equals(draft.WorkspaceMode, NEW_PROJECT_MODE, StringComparison.OrdinalIgnoreCase))
        {
            fields.Insert(3, new SetupField("ProjectName", "Project Name", string.IsNullOrWhiteSpace(draft.ProjectName) ? NONE_TEXT : draft.ProjectName));
        }

        fields.Add(new SetupField("__section__Advanced", "Advanced", ""));
        fields.Add(new SetupField("ArchitectureLoopMode", "Arch Loop Mode", draft.ArchitectureLoopMode ? "on" : "off"));

        if (draft.ArchitectureLoopMode)
        {
            bool loopPromptIsPlaceholder = string.IsNullOrWhiteSpace(draft.ArchitectureLoopPrompt);
            string loopPromptDisplay = loopPromptIsPlaceholder
                ? "Describe any specific architectural concerns or guidelines to focus on..."
                : draft.ArchitectureLoopPrompt!;
            fields.Add(new SetupField("ArchitectureLoopPrompt", "Arch Review Prompt", loopPromptDisplay, loopPromptIsPlaceholder));
        }

        return fields;
    }

    /// <summary>
    /// Builds a RunRequest from the finalized draft.
    /// </summary>
    /// <param name="draft">The finalized draft.</param>
    /// <returns>The constructed RunRequest.</returns>
    public static RunRequest BuildRequestFromDraft(SetupDraft draft)
    {
        string workspacePath = string.IsNullOrWhiteSpace(draft.WorkspacePath) ? Directory.GetCurrentDirectory() : draft.WorkspacePath;
        EnsureWorkspaceExists(workspacePath);

        return new RunRequest(
            TaskPrompt: ResolveTaskPrompt(draft),
            WorkspacePath: workspacePath,
            WorkspaceMode: string.IsNullOrWhiteSpace(draft.WorkspaceMode) ? EXISTING_FOLDER_MODE : draft.WorkspaceMode,
            Workflow: draft.ArchitectureLoopMode ? "architecture-loop" : "auto",
            ProjectName: string.IsNullOrWhiteSpace(draft.ProjectName) ? null : draft.ProjectName,
            ModelOverrides: CliArgumentParser.ParseOverrides(draft.ModelOverrides),
            BuildCommand: string.IsNullOrWhiteSpace(draft.BuildCommand) ? null : draft.BuildCommand,
            ArchitectureLoopMode: draft.ArchitectureLoopMode,
            ArchitectureLoopPrompt: string.IsNullOrWhiteSpace(draft.ArchitectureLoopPrompt) ? null : draft.ArchitectureLoopPrompt);
    }

    private static string ResolveTaskPrompt(SetupDraft draft)
    {
        if (draft.ArchitectureLoopMode)
        {
            return string.IsNullOrWhiteSpace(draft.TaskPrompt)
                ? DEFAULT_ARCH_LOOP_TASK_PROMPT
                : draft.TaskPrompt;
        }

        return string.IsNullOrWhiteSpace(draft.TaskPrompt) ? "Implement requested change" : draft.TaskPrompt;
    }

    private static void EnsureWorkspaceExists(string workspacePath)
    {
        string fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(workspacePath));
        Directory.CreateDirectory(fullPath);
    }
}
