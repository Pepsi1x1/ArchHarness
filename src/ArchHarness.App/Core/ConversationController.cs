using Microsoft.Extensions.Options;

namespace ArchHarness.App.Core;

/// <summary>
/// Coordinates the interactive setup form and CLI argument parsing to produce a RunRequest.
/// Delegates to CliArgumentParser, SetupFieldEditor, SetupNavigator, and SetupSummaryGenerator.
/// </summary>
public sealed class ConversationController
{
    private const string ExistingFolderMode = "existing-folder";

    private static string? _validationError;

    private readonly SetupSummaryGenerator _summaryGenerator;
    private readonly AgentsOptions _agentsOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConversationController"/> class.
    /// </summary>
    public ConversationController(SetupSummaryGenerator summaryGenerator, IOptions<AgentsOptions> agentsOptions)
    {
        _summaryGenerator = summaryGenerator;
        _agentsOptions = agentsOptions.Value;
    }

    /// <summary>
    /// Builds a RunRequest from CLI arguments or interactive setup.
    /// </summary>
    public async Task<(RunRequest Request, string SetupSummary)> BuildRunRequestAsync(string[] args, CancellationToken cancellationToken = default)
    {
        RunRequest? cliRequest = CliArgumentParser.TryParseCliArgs(args, _agentsOptions);
        if (cliRequest is not null)
        {
            string setupSummary = await _summaryGenerator.GenerateSetupSummaryAsync(cliRequest, cancellationToken);
            return (cliRequest, setupSummary);
        }

        RunRequest requestInteractive = BuildInteractiveRequest(
            _agentsOptions.Architecture.ArchitectureLoopMode,
            CliArgumentParser.NormalizeArchitectureLoopPrompt(_agentsOptions.Architecture.ArchitectureLoopPrompt));

        BuildCommandSelection setupSelection = BuildCommandInference.Select(
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
            await _summaryGenerator.RunIntentExtractionAsync(requestInteractive, cancellationToken);
        }
        catch
        {
            // Non-fatal: intent extraction is advisory only for setup UX.
        }

        string summary;
        try
        {
            summary = await _summaryGenerator.GenerateSetupSummaryAsync(requestInteractive, cancellationToken);
        }
        catch (Exception ex)
        {
            summary = $"Copilot summary unavailable ({ex.Message}). Proceeding with provided setup values.";
        }

        Console.WriteLine("[Chat/Setup Confirmation]");
        Console.WriteLine(summary);

        return (requestInteractive, summary);
    }

    private static RunRequest BuildInteractiveRequest(bool architectureLoopMode, string? architectureLoopPrompt)
    {
        if (Console.IsInputRedirected)
        {
            throw new InvalidOperationException(
                "Interactive setup requires a TTY-enabled stdin. Run with command-line arguments (`run <task> <workspacePath> <workspaceMode> ...`) when stdin is redirected.");
        }

        SetupDraft draft = new SetupDraft
        {
            TaskPrompt = architectureLoopMode ? string.Empty : "Implement requested change",
            WorkspacePath = Directory.GetCurrentDirectory(),
            WorkspaceMode = ExistingFolderMode,
            ArchitectureLoopMode = architectureLoopMode,
            ArchitectureLoopPrompt = architectureLoopPrompt
        };

        int selectedIndex = 0;
        while (true)
        {
            List<SetupField> fields = SetupFieldEditor.BuildFields(draft);
            if (selectedIndex >= fields.Count)
            {
                selectedIndex = fields.Count - 1;
            }

            SetupFormRenderer.RenderSetupForm(fields, selectedIndex, _validationError);
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            _validationError = null;

            if (SetupNavigator.TryHandleNavigation(key.Key, fields, ref selectedIndex))
            {
                continue;
            }

            if (SetupNavigator.TryHandleModeToggle(key.Key, fields[selectedIndex], draft))
            {
                continue;
            }

            if (TryHandleActionKey(key.Key, fields[selectedIndex].Id, draft, out RunRequest? completedRequest))
            {
                if (completedRequest is not null)
                {
                    return completedRequest;
                }

                continue;
            }
        }
    }

    private static bool TryHandleActionKey(ConsoleKey key, string fieldId, SetupDraft draft, out RunRequest? completedRequest)
    {
        completedRequest = null;

        if (key == ConsoleKey.Enter)
        {
            SetupFieldEditor.ApplyEdit(fieldId, draft);
            SetupFormRenderer.FlashSaved();
            return true;
        }

        if (key == ConsoleKey.F5)
        {
            string? errorFieldId = SetupFieldEditor.ValidateRequiredFields(draft);
            if (errorFieldId != null)
            {
                _validationError = errorFieldId;
                return true;
            }

            completedRequest = SetupFieldEditor.BuildRequestFromDraft(draft);
            return true;
        }

        if (key == ConsoleKey.Escape)
        {
            throw new OperationCanceledException("Run setup canceled by user.");
        }

        return false;
    }
}
