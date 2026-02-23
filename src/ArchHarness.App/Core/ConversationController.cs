using System.Text;
using ArchHarness.App.Copilot;

namespace ArchHarness.App.Core;

public sealed class ConversationController
{
    private const string NoneText = "(none)";
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
            WorkspaceMode = "existing-folder"
        };

        var selectedIndex = 0;
        while (true)
        {
            var fields = BuildFields(draft);
            if (selectedIndex >= fields.Count)
            {
                selectedIndex = fields.Count - 1;
            }

            RenderSetupForm(fields, selectedIndex);
            var key = Console.ReadKey(intercept: true);

            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    selectedIndex = selectedIndex == 0 ? fields.Count - 1 : selectedIndex - 1;
                    break;
                case ConsoleKey.DownArrow:
                    selectedIndex = selectedIndex == fields.Count - 1 ? 0 : selectedIndex + 1;
                    break;
                case ConsoleKey.LeftArrow:
                case ConsoleKey.RightArrow:
                    if (fields[selectedIndex].Id == "WorkspaceMode")
                    {
                        draft.WorkspaceMode = NextMode(draft.WorkspaceMode, key.Key == ConsoleKey.RightArrow ? 1 : -1);
                    }
                    break;
                case ConsoleKey.Enter:
                    ApplyEdit(fields[selectedIndex].Id, draft);
                    break;
                case ConsoleKey.F5:
                    var workspacePath = string.IsNullOrWhiteSpace(draft.WorkspacePath) ? Directory.GetCurrentDirectory() : draft.WorkspacePath;
                    EnsureWorkspaceExists(workspacePath);
                    return new RunRequest(
                        TaskPrompt: string.IsNullOrWhiteSpace(draft.TaskPrompt) ? "Implement requested change" : draft.TaskPrompt,
                        WorkspacePath: workspacePath,
                        WorkspaceMode: string.IsNullOrWhiteSpace(draft.WorkspaceMode) ? "existing-folder" : draft.WorkspaceMode,
                        Workflow: "auto",
                        ProjectName: string.IsNullOrWhiteSpace(draft.ProjectName) ? null : draft.ProjectName,
                        ModelOverrides: ParseOverrides(draft.ModelOverrides),
                        BuildCommand: string.IsNullOrWhiteSpace(draft.BuildCommand) ? null : draft.BuildCommand);
                case ConsoleKey.Escape:
                    throw new OperationCanceledException("Run setup canceled by user.");
            }
        }
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

        if (string.Equals(draft.WorkspaceMode, "new-project", StringComparison.OrdinalIgnoreCase))
        {
            fields.Insert(3, new SetupField("ProjectName", "Project Name", string.IsNullOrWhiteSpace(draft.ProjectName) ? NoneText : draft.ProjectName));
        }

        return fields;
    }

    private static void RenderSetupForm(IReadOnlyList<SetupField> fields, int selectedIndex)
    {
        Console.Clear();
        Console.WriteLine("=== Chat / Setup ===");
        Console.WriteLine("Use Up/Down to navigate, Enter to edit, Left/Right to change mode, F5 to run, Esc to cancel.");
        Console.WriteLine("Workspace Path editor supports Tab completion.");
        Console.WriteLine();

        for (var i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            var marker = i == selectedIndex ? ">" : " ";
            Console.WriteLine($"{marker} {field.Label,-16}: {field.Value}");
        }
    }

    private static void ApplyEdit(string fieldId, SetupDraft draft)
    {
        if (fieldId == "WorkspaceMode")
        {
            draft.WorkspaceMode = NextMode(draft.WorkspaceMode, 1);
            return;
        }

        Console.SetCursorPosition(0, Console.CursorTop + 2);
        Console.Write($"Edit {fieldId}> ");
        var value = fieldId == "WorkspacePath"
            ? ReadPathWithTabCompletion(draft.WorkspacePath)
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
        var modes = new[] { "new-project", "existing-folder", "existing-git" };
        var currentIndex = Array.FindIndex(modes, m => string.Equals(m, currentMode, StringComparison.OrdinalIgnoreCase));
        if (currentIndex < 0)
        {
            currentIndex = 1;
        }

        var next = (currentIndex + delta + modes.Length) % modes.Length;
        return modes[next];
    }

    private static string ReadPathWithTabCompletion(string currentValue)
    {
        var buffer = new StringBuilder(currentValue ?? string.Empty);
        Console.Write(buffer.ToString());

        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return buffer.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Length--;
                    Console.Write("\b \b");
                }
                continue;
            }

            if (key.Key == ConsoleKey.Tab)
            {
                var completed = TryCompletePath(buffer.ToString());
                if (!string.Equals(completed, buffer.ToString(), StringComparison.Ordinal))
                {
                    while (buffer.Length > 0)
                    {
                        buffer.Length--;
                        Console.Write("\b \b");
                    }

                    buffer.Append(completed);
                    Console.Write(completed);
                }
                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                buffer.Append(key.KeyChar);
                Console.Write(key.KeyChar);
            }
        }
    }

    private static string TryCompletePath(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return input;
        }

        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(input);
            var directoryPart = expanded;
            var prefix = string.Empty;

            if (!Directory.Exists(expanded))
            {
                directoryPart = Path.GetDirectoryName(expanded) ?? Directory.GetCurrentDirectory();
                prefix = Path.GetFileName(expanded) ?? string.Empty;
            }

            if (!Directory.Exists(directoryPart))
            {
                return input;
            }

            var matches = Directory.GetDirectories(directoryPart)
                .Where(d => Path.GetFileName(d).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (matches.Length == 0)
            {
                return input;
            }

            var match = matches[0];
            return match.EndsWith(Path.DirectorySeparatorChar)
                ? match
                : match + Path.DirectorySeparatorChar;
        }
        catch
        {
            return input;
        }
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
        _ = await _copilotClient.CompleteAsync(model, prompt, cancellationToken);
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

        var completion = await _copilotClient.CompleteAsync(model, prompt, cancellationToken);
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
        public string WorkspaceMode { get; set; } = "existing-folder";
        public string? ProjectName { get; set; }
        public string? ModelOverrides { get; set; }
        public string? BuildCommand { get; set; }
    }

    private sealed record SetupField(string Id, string Label, string Value);
}
