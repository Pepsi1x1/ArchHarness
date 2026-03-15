using Microsoft.Extensions.Options;

namespace ArchHarness.App.Core;

/// <summary>
/// Coordinates the interactive setup form and CLI argument parsing to produce a RunRequest.
/// Delegates to CliArgumentParser, SetupFieldEditor, SetupNavigator, and SetupSummaryGenerator.
/// </summary>
public sealed class ConversationController
{
    private const string EXISTING_FOLDER_MODE = "existing-folder";

    private readonly SetupSummaryGenerator _summaryGenerator;
    private readonly AgentsOptions _agentsOptions;
    private readonly IModelResolver _modelResolver;
    private readonly RuntimeStateAccessors _stateAccessors;
    private readonly ISetupStatusSink _statusSink;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConversationController"/> class.
    /// </summary>
    public ConversationController(SetupSummaryGenerator summaryGenerator, IOptions<AgentsOptions> agentsOptions, IModelResolver modelResolver, RuntimeStateAccessors stateAccessors, ISetupStatusSink statusSink)
    {
        this._summaryGenerator = summaryGenerator;
        this._agentsOptions = agentsOptions.Value;
        this._modelResolver = modelResolver;
        this._stateAccessors = stateAccessors;
        this._statusSink = statusSink;
    }

    /// <summary>
    /// Builds a RunRequest from CLI arguments or interactive setup.
    /// </summary>
    public async Task<(RunRequest Request, string SetupSummary)> BuildRunRequestAsync(string[] args, CancellationToken cancellationToken = default)
    {
        RunRequest? cliRequest = CliArgumentParser.TryParseCliArgs(args, this._agentsOptions);
        if (cliRequest is not null)
        {
            cliRequest = await this._summaryGenerator.PopulateRunTitleAsync(cliRequest, cancellationToken);
            this._stateAccessors.PermissionHandlerMode.SetCurrent(PermissionHandlerModes.Normalize(cliRequest.PermissionHandlerMode));
            this._stateAccessors.ReviewLoopAgentSelection.SetCurrent(ResolveReviewLoopAgents(cliRequest, this._agentsOptions));
            this._stateAccessors.WorkspaceRoot.SetCurrent(ResolveWorkspaceRoot(cliRequest.WorkspacePath));
            this._modelResolver.ValidateConfiguredModelsOrThrow(cliRequest.ModelOverrides);
            string setupSummary = await this._summaryGenerator.GenerateSetupSummaryAsync(cliRequest, cancellationToken);
            return (cliRequest, setupSummary);
        }

        RunRequest requestInteractive = BuildInteractiveRequest(
            this._agentsOptions.GetReviewLoopAgentSelection(),
            this._agentsOptions.Architecture.ArchitectureLoopMode,
            CliArgumentParser.NormalizeArchitectureLoopPrompt(this._agentsOptions.Architecture.ArchitectureLoopPrompt));

        this._statusSink.Clear();
        this._statusSink.WriteLine("Preparing run configuration...");
        this._stateAccessors.PermissionHandlerMode.SetCurrent(PermissionHandlerModes.Normalize(requestInteractive.PermissionHandlerMode));
        this._stateAccessors.ReviewLoopAgentSelection.SetCurrent(ResolveReviewLoopAgents(requestInteractive, this._agentsOptions));
        this._stateAccessors.WorkspaceRoot.SetCurrent(ResolveWorkspaceRoot(requestInteractive.WorkspacePath));
        this._modelResolver.ValidateConfiguredModelsOrThrow(requestInteractive.ModelOverrides);
        this._statusSink.WriteLine("Contacting Copilot for intent extraction and setup summary.");

        try
        {
            await this._summaryGenerator.RunIntentExtractionAsync(requestInteractive, cancellationToken);
        }
        catch
        {
            // Non-fatal: intent extraction is advisory only for setup UX.
        }

        requestInteractive = await this._summaryGenerator.PopulateRunTitleAsync(requestInteractive, cancellationToken);

        string summary;
        try
        {
            summary = await this._summaryGenerator.GenerateSetupSummaryAsync(requestInteractive, cancellationToken);
        }
        catch (Exception ex)
        {
            summary = $"Copilot summary unavailable ({ex.Message}). Proceeding with provided setup values.";
        }

        this._statusSink.WriteLine("[Chat/Setup Confirmation]");
        this._statusSink.WriteLine(summary);

        return (requestInteractive, summary);
    }

    private static RunRequest BuildInteractiveRequest(ReviewLoopAgentSelection reviewLoopAgents, bool architectureLoopMode, string? architectureLoopPrompt)
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
            WorkspaceMode = EXISTING_FOLDER_MODE,
            PermissionHandlerMode = PermissionHandlerModes.ApproveAll,
            ReviewLoopCodingStyleEnabled = reviewLoopAgents.CodingStyleEnabled,
            ReviewLoopSecurityEnabled = reviewLoopAgents.SecurityEnabled,
            ReviewLoopArchitectureEnabled = reviewLoopAgents.ArchitectureEnabled,
            ArchitectureLoopMode = architectureLoopMode,
            ArchitectureLoopPrompt = architectureLoopPrompt
        };

        int selectedIndex = 0;
        string? validationError = null;
        while (true)
        {
            List<SetupField> fields = SetupFieldEditor.BuildFields(draft);
            if (selectedIndex >= fields.Count)
            {
                selectedIndex = fields.Count - 1;
            }

            SetupFormRenderer.RenderSetupForm(fields, selectedIndex, validationError);
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            validationError = null;

            if (SetupNavigator.TryHandleNavigation(key.Key, fields, ref selectedIndex))
            {
                continue;
            }

            if (SetupNavigator.TryHandleModeToggle(key.Key, fields[selectedIndex], draft))
            {
                continue;
            }

            if (TryHandleActionKey(key.Key, fields[selectedIndex].Id, draft, ref validationError, out RunRequest? completedRequest))
            {
                if (completedRequest is not null)
                {
                    return completedRequest;
                }

                continue;
            }
        }
    }

    private static bool TryHandleActionKey(ConsoleKey key, string fieldId, SetupDraft draft, ref string? validationError, out RunRequest? completedRequest)
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
                validationError = errorFieldId;
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

    private static string ResolveWorkspaceRoot(string workspacePath)
        => Path.GetFullPath(Environment.ExpandEnvironmentVariables(workspacePath));

    private static ReviewLoopAgentSelection ResolveReviewLoopAgents(RunRequest request, AgentsOptions agentsOptions)
        => request.ReviewLoopAgents ?? agentsOptions.GetReviewLoopAgentSelection();
}
