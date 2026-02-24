using System.Text;
using ArchHarness.App.Copilot;

namespace ArchHarness.App.Core;

public sealed class ConversationController
{
    private const string NoneText = "(none)";
    private const string ExistingFolderMode = "existing-folder";
    private const string NewProjectMode = "new-project";
    private const string ExistingGitMode = "existing-git";

    private const string WorkspaceModeField = "WorkspaceMode";
    private const string WorkspacePathField = "WorkspacePath";

    private static string? _validationError;

    private readonly ICopilotClient _copilotClient;
    private readonly IModelResolver _modelResolver;

    public ConversationController(ICopilotClient copilotClient, IModelResolver modelResolver)
    {
        _copilotClient = copilotClient;
        _modelResolver = modelResolver;
    }

    public async Task<(RunRequest Request, string SetupSummary)> BuildRunRequestAsync(string[] args, CancellationToken cancellationToken = default)
    {
        if (args.Length >= 4 && args[0].Equals("run", StringComparison.OrdinalIgnoreCase))
        {
            var request = new RunRequest(
                TaskPrompt: args[1],
                WorkspacePath: args[2],
                WorkspaceMode: args[3],
                Workflow: args.Length >= 5 ? args[4] : "frontend_feature",
                ProjectName: args.Length >= 6 ? args[5] : null,
                ModelOverrides: args.Length >= 7 ? ParseOverrides(args[6]) : null,
                BuildCommand: args.Length >= 8 ? args[7] : null);

            var setupSummary = await GenerateSetupSummaryAsync(request, cancellationToken);
            return (request, setupSummary);
        }

        var requestInteractive = BuildInteractiveRequest();
        var setupSelection = BuildCommandInference.Select(
            requestInteractive.WorkspacePath,
            requestInteractive.BuildCommand,
            requestInteractive.WorkspaceMode,
            requestInteractive.ProjectName);
        if (!string.Equals(setupSelection.Command, requestInteractive.BuildCommand, StringComparison.Ordinal))
        {
            requestInteractive = requestInteractive with { BuildCommand = setupSelection.Command };
        }

        Console.Clear();
        Console.WriteLine("Preparing run configuration...");
        Console.WriteLine("Contacting Copilot for intent extraction and setup summary.");

        try
        {
            await RunIntentExtractionAsync(requestInteractive, cancellationToken);
        }
        catch
        {
            // Non-fatal: intent extraction is advisory only for setup UX.
        }

        string summary;
        try
        {
            summary = await GenerateSetupSummaryAsync(requestInteractive, cancellationToken);
        }
        catch (Exception ex)
        {
            summary = $"Copilot summary unavailable ({ex.Message}). Proceeding with provided setup values.";
        }

        Console.WriteLine("[Chat/Setup Confirmation]");
        Console.WriteLine(summary);

        return (requestInteractive, summary);
    }

    private static RunRequest BuildInteractiveRequest()
    {
        var draft = new SetupDraft
        {
            TaskPrompt = "Implement requested change",
            WorkspacePath = Directory.GetCurrentDirectory(),
            WorkspaceMode = ExistingFolderMode
        };

        var selectedIndex = 0;
        while (true)
        {
            var fields = BuildFields(draft);
            if (selectedIndex >= fields.Count)
            {
                selectedIndex = fields.Count - 1;
            }

            SetupFormRenderer.RenderSetupForm(fields, selectedIndex, _validationError);
            var key = Console.ReadKey(intercept: true);
            _validationError = null;

            if (TryHandleNavigation(key.Key, fields.Count, ref selectedIndex))
            {
                continue;
            }

            if (TryHandleModeToggle(key.Key, fields[selectedIndex], draft))
            {
                continue;
            }

            if (key.Key == ConsoleKey.Enter)
            {
                ApplyEdit(fields[selectedIndex].Id, draft);
                SetupFormRenderer.FlashSaved();
                continue;
            }

            if (key.Key == ConsoleKey.F5)
            {
                var errorFieldId = ValidateRequiredFields(draft);
                if (errorFieldId != null)
                {
                    _validationError = errorFieldId;
                    continue;
                }

                return BuildRequestFromDraft(draft);
            }

            if (key.Key == ConsoleKey.Escape)
            {
                throw new OperationCanceledException("Run setup canceled by user.");
            }
        }
    }

    private static bool TryHandleNavigation(ConsoleKey key, int fieldCount, ref int selectedIndex)
    {
        if (key == ConsoleKey.UpArrow)
        {
            selectedIndex = selectedIndex == 0 ? fieldCount - 1 : selectedIndex - 1;
            return true;
        }

        if (key == ConsoleKey.DownArrow)
        {
            selectedIndex = selectedIndex == fieldCount - 1 ? 0 : selectedIndex + 1;
            return true;
        }

        return false;
    }

    private static bool TryHandleModeToggle(ConsoleKey key, SetupField field, SetupDraft draft)
    {
        if (key is not (ConsoleKey.LeftArrow or ConsoleKey.RightArrow))
        {
            return false;
        }

        if (field.Id != WorkspaceModeField)
        {
            return false;
        }

        draft.WorkspaceMode = NextMode(draft.WorkspaceMode, key == ConsoleKey.RightArrow ? 1 : -1);
        return true;
    }

    private static RunRequest BuildRequestFromDraft(SetupDraft draft)
    {
        var workspacePath = string.IsNullOrWhiteSpace(draft.WorkspacePath) ? Directory.GetCurrentDirectory() : draft.WorkspacePath;
        EnsureWorkspaceExists(workspacePath);

        return new RunRequest(
            TaskPrompt: string.IsNullOrWhiteSpace(draft.TaskPrompt) ? "Implement requested change" : draft.TaskPrompt,
            WorkspacePath: workspacePath,
            WorkspaceMode: string.IsNullOrWhiteSpace(draft.WorkspaceMode) ? ExistingFolderMode : draft.WorkspaceMode,
            Workflow: "auto",
            ProjectName: string.IsNullOrWhiteSpace(draft.ProjectName) ? null : draft.ProjectName,
            ModelOverrides: ParseOverrides(draft.ModelOverrides),
            BuildCommand: string.IsNullOrWhiteSpace(draft.BuildCommand) ? null : draft.BuildCommand);
    }

    private static List<SetupField> BuildFields(SetupDraft draft)
    {
        var fields = new List<SetupField>
        {
            new("TaskPrompt", "Task", draft.TaskPrompt),
            new("WorkspacePath", "Workspace Path", draft.WorkspacePath),
            new("WorkspaceMode", "Workspace Mode", draft.WorkspaceMode),
            new("ModelOverrides", "Model Overrides", string.IsNullOrWhiteSpace(draft.ModelOverrides) ? NoneText : draft.ModelOverrides),
            new("BuildCommand", "Build Command", string.IsNullOrWhiteSpace(draft.BuildCommand) ? NoneText : draft.BuildCommand)
        };

        if (string.Equals(draft.WorkspaceMode, NewProjectMode, StringComparison.OrdinalIgnoreCase))
        {
            fields.Insert(3, new SetupField("ProjectName", "Project Name", string.IsNullOrWhiteSpace(draft.ProjectName) ? NoneText : draft.ProjectName));
        }

        return fields;
    }



    // Returns the field ID of the first failing required field, or null if all pass.
    private static string? ValidateRequiredFields(SetupDraft draft)
    {
        return string.IsNullOrWhiteSpace(draft.TaskPrompt) ? "TaskPrompt" : null;
    }

    private static void ApplyEdit(string fieldId, SetupDraft draft)
    {
        if (fieldId == WorkspaceModeField)
        {
            draft.WorkspaceMode = NextMode(draft.WorkspaceMode, 1);
            return;
        }

        Console.SetCursorPosition(0, Console.CursorTop + 2);
        Console.Write($"Edit {fieldId}> ");
        var value = fieldId == WorkspacePathField
            ? PathInputHandler.ReadPathWithTabCompletion(draft.WorkspacePath)
            : Console.ReadLine();
        if (value is null)
        {
            return;
        }

        switch (fieldId)
        {
            case "TaskPrompt":
                draft.TaskPrompt = value;
                break;
            case "WorkspacePath":
                draft.WorkspacePath = value;
                break;
            case "ProjectName":
                draft.ProjectName = value;
                break;
            case "Workflow":
            case "ModelOverrides":
                draft.ModelOverrides = value;
                break;
            case "BuildCommand":
                draft.BuildCommand = value;
                break;
        }
    }

    private static string NextMode(string currentMode, int delta)
    {
        var modes = new[] { NewProjectMode, ExistingFolderMode, ExistingGitMode };
        var currentIndex = Array.FindIndex(modes, m => string.Equals(m, currentMode, StringComparison.OrdinalIgnoreCase));
        if (currentIndex < 0)
        {
            currentIndex = 1;
        }

        var next = (currentIndex + delta + modes.Length) % modes.Length;
        return modes[next];
    }


    private static void EnsureWorkspaceExists(string workspacePath)
    {
        var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(workspacePath));
        Directory.CreateDirectory(fullPath);
    }

    private async Task RunIntentExtractionAsync(RunRequest request, CancellationToken cancellationToken)
    {
        var model = _modelResolver.Resolve("conversation", request.ModelOverrides);
        var prompt = $"""
            Extract intent for this run request and identify missing optional fields.
            Return a compact one-line summary.
            Task: {request.TaskPrompt}
            Workflow: {request.Workflow}
            WorkspaceMode: {request.WorkspaceMode}
            ProjectName: {request.ProjectName ?? NoneText}
            BuildCommand: {request.BuildCommand ?? NoneText}
            """;
        _ = await _copilotClient.CompleteAsync(model, prompt, cancellationToken: cancellationToken);
    }

    private async Task<string> GenerateSetupSummaryAsync(RunRequest request, CancellationToken cancellationToken)
    {
        var model = _modelResolver.Resolve("conversation", request.ModelOverrides);
        var prompt = $"""
            Summarize this run configuration in 4 concise bullet points.
            Task: {request.TaskPrompt}
            WorkspacePath: {request.WorkspacePath}
            WorkspaceMode: {request.WorkspaceMode}
            Workflow: {request.Workflow}
            ProjectName: {request.ProjectName ?? NoneText}
            BuildCommand: {request.BuildCommand ?? NoneText}
            Overrides: {FormatOverrides(request.ModelOverrides)}
            """;

        var completion = await _copilotClient.CompleteAsync(model, prompt, cancellationToken: cancellationToken);
        return Redaction.RedactSecrets(completion);
    }

    private static IDictionary<string, string>? ParseOverrides(string? overrideText)
    {
        if (string.IsNullOrWhiteSpace(overrideText))
        {
            return null;
        }

        var output = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var segments = overrideText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var segment in segments)
        {
            var idx = segment.IndexOf('=');
            if (idx <= 0 || idx == segment.Length - 1)
            {
                continue;
            }

            var role = segment[..idx].Trim();
            var model = segment[(idx + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(role) && !string.IsNullOrWhiteSpace(model))
            {
                output[role] = model;
            }
        }

        return output.Count == 0 ? null : output;
    }

    private static string FormatOverrides(IDictionary<string, string>? overrides)
    {
        if (overrides is null || overrides.Count == 0)
        {
            return NoneText;
        }

        var builder = new StringBuilder();
        foreach (var pair in overrides)
        {
            if (builder.Length > 0)
            {
                builder.Append(", ");
            }

            builder.Append(pair.Key).Append('=').Append(pair.Value);
        }

        return builder.ToString();
    }

    private sealed class SetupDraft
    {
        public string TaskPrompt { get; set; } = string.Empty;
        public string WorkspacePath { get; set; } = string.Empty;
        public string WorkspaceMode { get; set; } = ExistingFolderMode;
        public string? ProjectName { get; set; }
        public string? ModelOverrides { get; set; }
        public string? BuildCommand { get; set; }
    }
}
