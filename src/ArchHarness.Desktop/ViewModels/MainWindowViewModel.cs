using System.Collections.ObjectModel;
using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using Microsoft.Extensions.Options;

namespace ArchHarness.Desktop.ViewModels;

/// <summary>
/// Primary view model for the desktop main window, managing run lifecycle,
/// artifact inspection, and setup configuration.
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase
{
    private const string DEFAULT_TASK_PROMPT = "Implement requested change";
    private const string DEFAULT_ARCH_LOOP_TASK_PROMPT = "Run coding style, security, and architecture review loop for the existing workspace and apply required remediation.";

    private readonly IRunHistoryService _runHistoryService;
    private readonly OrchestratorRuntime _runtime;
    private readonly RunStreamingCoordinator _streamingCoordinator;
    private readonly IStartupPreflightValidator _preflightValidator;
    private readonly SetupSummaryGenerator _summaryGenerator;
    private string _workspacePath = Environment.CurrentDirectory;
    private string _taskPrompt = DEFAULT_TASK_PROMPT;
    private string _workflow = "auto";
    private string _workspaceMode = "existing-folder";
    private string _permissionHandlerMode = "approve-all";
    private string _projectName = string.Empty;
    private string _modelOverridesText = string.Empty;
    private string _buildCommand = string.Empty;
    private string _architectureLoopPrompt = string.Empty;
    private string _setupSummary = "Generate a run summary to preview the request that will be sent to the orchestrator.";
    private string _runStatus = "Idle";
    private string _setupValidationMessage = string.Empty;
    private string _preflightStatusTitle = "Checking local runtime";
    private string _preflightStatusDetail = "Verifying Copilot CLI availability and authentication.";
    private string _selectedArtifactPreview = "Select an artefact to preview its contents.";
    private string _selectedAgentTranscript = "Run a session to stream agent output here.";
    private RunSummaryViewModel? _selectedRun;
    private ArtifactItemViewModel? _selectedArtifact;
    private AgentItemViewModel? _selectedAgent;
    private bool _initialized;
    private bool _reviewLoopCodingStyleEnabled;
    private bool _reviewLoopSecurityEnabled;
    private bool _reviewLoopArchitectureEnabled;
    private bool _architectureLoopMode;
    private bool _isRunInProgress;

    private MainWindowViewModel()
    {
        this._runHistoryService = null!;
        this._runtime = null!;
        this._streamingCoordinator = null!;
        this._preflightValidator = null!;
        this._summaryGenerator = null!;
        this.RecentRuns = new ObservableCollection<RunSummaryViewModel>();
        this.Artifacts = new ObservableCollection<ArtifactItemViewModel>();
        this.TimelineItems = new ObservableCollection<TimelineItemViewModel>();
        this.AvailableAgents = new ObservableCollection<AgentItemViewModel>();
        this.SessionEvents = new ObservableCollection<SessionEventItemViewModel>();
        this.WorkspaceModes = new[] { "existing-folder", "new-project", "existing-git" };
        this.PermissionModes = new[] { "approve-all", "prompt" };
        this._taskPrompt = DEFAULT_TASK_PROMPT;
        this._workflow = "auto";
        this._setupSummary = "Design-time preview";
        TimelineBuilder.SeedEmpty(this.TimelineItems);
    }
    private CancellationTokenSource? _runCts;

    public MainWindowViewModel(
        IRunHistoryService runHistoryService,
        OrchestratorRuntime runtime,
        RunStreamingCoordinator streamingCoordinator,
        IStartupPreflightValidator preflightValidator,
        SetupSummaryGenerator summaryGenerator,
        IOptions<AgentsOptions> agentsOptions)
    {
        this._runHistoryService = runHistoryService;
        this._runtime = runtime;
        this._streamingCoordinator = streamingCoordinator;
        this._preflightValidator = preflightValidator;
        this._summaryGenerator = summaryGenerator;
        this.RecentRuns = new ObservableCollection<RunSummaryViewModel>();
        this.Artifacts = new ObservableCollection<ArtifactItemViewModel>();
        this.TimelineItems = new ObservableCollection<TimelineItemViewModel>();
        this.AvailableAgents = new ObservableCollection<AgentItemViewModel>();
        this.SessionEvents = new ObservableCollection<SessionEventItemViewModel>();
        AgentsOptions config = agentsOptions.Value;
        ReviewLoopAgentSelection reviewLoopSelection = config.GetReviewLoopAgentSelection();
        this._reviewLoopCodingStyleEnabled = reviewLoopSelection.CodingStyleEnabled;
        this._reviewLoopSecurityEnabled = reviewLoopSelection.SecurityEnabled;
        this._reviewLoopArchitectureEnabled = reviewLoopSelection.ArchitectureEnabled;
        this._architectureLoopMode = config.Architecture.ArchitectureLoopMode;
        this._architectureLoopPrompt = config.Architecture.ArchitectureLoopPrompt ?? string.Empty;
        this._taskPrompt = this._architectureLoopMode ? DEFAULT_ARCH_LOOP_TASK_PROMPT : DEFAULT_TASK_PROMPT;
        this._workflow = this._architectureLoopMode ? "architecture-loop" : "auto";
        this.WorkspaceModes = new[] { "existing-folder", "new-project", "existing-git" };
        this.PermissionModes = new[] { "approve-all", "prompt" };
        TimelineBuilder.SeedEmpty(this.TimelineItems);
    }

    /// <summary>
    /// Creates a design-time preview instance populated with sample data.
    /// </summary>
    /// <returns>A view model instance suitable for XAML designer previews.</returns>
    public static MainWindowViewModel CreateDesignInstance()
    {
        MainWindowViewModel viewModel = new MainWindowViewModel();
        viewModel.RunStatus = "Design preview";
        viewModel.PreflightStatusTitle = "Design-time host";
        viewModel.PreflightStatusDetail = "This preview is generated without the runtime service graph.";
        viewModel.RecentRuns.Add(new RunSummaryViewModel("20260314T121500000", "/workspace/.agent-harness/runs/20260314T121500000"));
        ArtifactItemViewModel artifact = new ArtifactItemViewModel(
            "FinalSummary.md",
            "/workspace/.agent-harness/runs/20260314T121500000/FinalSummary.md",
            "Markdown",
            "Markdown review summary",
            "# Final Summary\n\n- Completed: true\n- Build passes\n- No high severity findings\n");
        viewModel.Artifacts.Add(artifact);
        viewModel.SelectedRun = viewModel.RecentRuns[0];
        viewModel.SelectArtifact(artifact);
        viewModel.AvailableAgents.Add(new AgentItemViewModel("architecture-01", "architecture"));
        viewModel.SelectedAgent = viewModel.AvailableAgents[0];
        viewModel.SelectedAgentTranscript = "Reviewing architecture findings and validating completion criteria...";
        viewModel.SessionEvents.Add(new SessionEventItemViewModel("Session created", "conversation", "gpt-5.4", "archharness-session", "Desktop design preview of Copilot session lifecycle output."));
        return viewModel;
    }

    /// <summary>Gets the collection of recent runs displayed in the left rail.</summary>
    public ObservableCollection<RunSummaryViewModel> RecentRuns { get; }

    /// <summary>Gets the collection of artifacts for the selected run.</summary>
    public ObservableCollection<ArtifactItemViewModel> Artifacts { get; }

    /// <summary>Gets the collection of timeline entries for the current session.</summary>
    public ObservableCollection<TimelineItemViewModel> TimelineItems { get; }

    /// <summary>Gets the collection of streaming agents discovered during a run.</summary>
    public ObservableCollection<AgentItemViewModel> AvailableAgents { get; }

    /// <summary>Gets the collection of Copilot session lifecycle events.</summary>
    public ObservableCollection<SessionEventItemViewModel> SessionEvents { get; }

    /// <summary>Gets the available workspace initialization modes.</summary>
    public IReadOnlyList<string> WorkspaceModes { get; }

    /// <summary>Gets the available permission approval modes.</summary>
    public IReadOnlyList<string> PermissionModes { get; }

    /// <summary>Gets or sets the workspace file-system path.</summary>
    public string WorkspacePath
    {
        get => this._workspacePath;
        set => this.SetProperty(ref this._workspacePath, value);
    }

    /// <summary>Gets or sets the user-supplied task prompt.</summary>
    public string TaskPrompt
    {
        get => this._taskPrompt;
        set => this.SetProperty(ref this._taskPrompt, value);
    }

    /// <summary>Gets or sets the workflow identifier that selects the execution pipeline.</summary>
    public string Workflow
    {
        get => this._workflow;
        set => this.SetProperty(ref this._workflow, value);
    }

    /// <summary>Gets or sets the workspace initialization mode.</summary>
    public string WorkspaceMode
    {
        get => this._workspaceMode;
        set => this.SetProperty(ref this._workspaceMode, value);
    }

    /// <summary>Gets or sets the permission approval mode for Copilot tool requests.</summary>
    public string PermissionHandlerMode
    {
        get => this._permissionHandlerMode;
        set => this.SetProperty(ref this._permissionHandlerMode, RunRequestFactory.NormalizePermissionMode(value));
    }

    /// <summary>Gets or sets the optional project name used when creating a new workspace.</summary>
    public string ProjectName
    {
        get => this._projectName;
        set => this.SetProperty(ref this._projectName, value);
    }

    /// <summary>Gets or sets the comma-separated model override text (e.g., "role=model,role=model").</summary>
    public string ModelOverridesText
    {
        get => this._modelOverridesText;
        set => this.SetProperty(ref this._modelOverridesText, value);
    }

    /// <summary>Gets or sets the optional build command to execute for validation.</summary>
    public string BuildCommand
    {
        get => this._buildCommand;
        set => this.SetProperty(ref this._buildCommand, value);
    }

    /// <summary>Gets or sets the optional supplementary prompt for architecture loop iterations.</summary>
    public string ArchitectureLoopPrompt
    {
        get => this._architectureLoopPrompt;
        set => this.SetProperty(ref this._architectureLoopPrompt, value);
    }

    /// <summary>Gets or sets whether coding style enforcement is enabled in the review loop.</summary>
    public bool ReviewLoopCodingStyleEnabled
    {
        get => this._reviewLoopCodingStyleEnabled;
        set => this.SetProperty(ref this._reviewLoopCodingStyleEnabled, value);
    }

    /// <summary>Gets or sets whether security review is enabled in the review loop.</summary>
    public bool ReviewLoopSecurityEnabled
    {
        get => this._reviewLoopSecurityEnabled;
        set => this.SetProperty(ref this._reviewLoopSecurityEnabled, value);
    }

    /// <summary>Gets or sets whether architecture review is enabled in the review loop.</summary>
    public bool ReviewLoopArchitectureEnabled
    {
        get => this._reviewLoopArchitectureEnabled;
        set => this.SetProperty(ref this._reviewLoopArchitectureEnabled, value);
    }

    /// <summary>Gets or sets whether iterative architecture review mode is active.</summary>
    public bool ArchitectureLoopMode
    {
        get => this._architectureLoopMode;
        set
        {
            if (this.SetProperty(ref this._architectureLoopMode, value))
            {
                this.TaskPrompt = value && string.IsNullOrWhiteSpace(this.TaskPrompt) ? DEFAULT_ARCH_LOOP_TASK_PROMPT : this.TaskPrompt;
                this.Workflow = value ? "architecture-loop" : "auto";
            }
        }
    }

    /// <summary>Gets a value indicating whether an orchestrated run is currently in progress.</summary>
    public bool IsRunInProgress
    {
        get => this._isRunInProgress;
        private set
        {
            if (this.SetProperty(ref this._isRunInProgress, value))
            {
                this.RaisePropertyChanged(nameof(this.CanStartRun));
                this.RaisePropertyChanged(nameof(this.CanCancelRun));
                this.RaisePropertyChanged(nameof(this.RunStateBadge));
            }
        }
    }

    /// <summary>Gets a value indicating whether a new run can be started.</summary>
    public bool CanStartRun => !this.IsRunInProgress;

    /// <summary>Gets a value indicating whether the active run can be canceled.</summary>
    public bool CanCancelRun => this.IsRunInProgress;

    /// <summary>Gets the current human-readable run status label.</summary>
    public string RunStatus
    {
        get => this._runStatus;
        private set
        {
            if (this.SetProperty(ref this._runStatus, value))
            {
                this.RaisePropertyChanged(nameof(this.RunStateBadge));
            }
        }
    }

    /// <summary>Gets the generated setup summary text.</summary>
    public string SetupSummary
    {
        get => this._setupSummary;
        private set => this.SetProperty(ref this._setupSummary, value);
    }

    /// <summary>Gets the current setup validation error message, if any.</summary>
    public string SetupValidationMessage
    {
        get => this._setupValidationMessage;
        private set
        {
            if (this.SetProperty(ref this._setupValidationMessage, value))
            {
                this.RaisePropertyChanged(nameof(this.HasSetupValidationMessage));
            }
        }
    }

    /// <summary>Gets a value indicating whether a setup validation message is present.</summary>
    public bool HasSetupValidationMessage => !string.IsNullOrWhiteSpace(this.SetupValidationMessage);

    /// <summary>Gets or sets the currently selected run in the left rail.</summary>
    public RunSummaryViewModel? SelectedRun
    {
        get => this._selectedRun;
        set
        {
            if (this.SetProperty(ref this._selectedRun, value))
            {
                this.RaisePropertyChanged(nameof(this.SelectedRunTitle));
                this.RaisePropertyChanged(nameof(this.SelectedRunDetail));
            }
        }
    }

    /// <summary>Gets the currently selected artifact, or <see langword="null"/> if none is selected.</summary>
    public ArtifactItemViewModel? SelectedArtifact
    {
        get => this._selectedArtifact;
        private set => this.SetProperty(ref this._selectedArtifact, value);
    }

    /// <summary>Gets or sets the currently selected streaming agent.</summary>
    public AgentItemViewModel? SelectedAgent
    {
        get => this._selectedAgent;
        set
        {
            if (this.SetProperty(ref this._selectedAgent, value))
            {
                this.RefreshSelectedAgentTranscript();
            }
        }
    }

    /// <summary>Gets the text preview of the selected artifact.</summary>
    public string SelectedArtifactPreview
    {
        get => this._selectedArtifactPreview;
        private set => this.SetProperty(ref this._selectedArtifactPreview, value);
    }

    /// <summary>Gets the accumulated transcript text of the selected streaming agent.</summary>
    public string SelectedAgentTranscript
    {
        get => this._selectedAgentTranscript;
        private set => this.SetProperty(ref this._selectedAgentTranscript, value);
    }

    /// <summary>Gets the headline text derived from the selected run.</summary>
    public string Headline => this.SelectedRun is null ? "Desktop run inspector" : this.SelectedRun.Title;

    /// <summary>Gets the contextual subheadline describing the current state.</summary>
    public string Subheadline
    {
        get
        {
            if (this.IsRunInProgress)
            {
                return "Live runtime progress, agent streaming output, and artefact generation are active in the desktop host.";
            }

            if (this.SelectedRun is null)
            {
                return "The desktop host can now launch runs, stream progress, and inspect persisted sessions from the same shell.";
            }

            return "Inspect persisted run artefacts or start a new orchestrated session from the setup panel.";
        }
    }

    /// <summary>Gets the preflight validation title label.</summary>
    public string PreflightStatusTitle
    {
        get => this._preflightStatusTitle;
        private set
        {
            if (this.SetProperty(ref this._preflightStatusTitle, value))
            {
                this.RaisePropertyChanged(nameof(this.PreflightBadge));
            }
        }
    }

    /// <summary>Gets the preflight validation detail text.</summary>
    public string PreflightStatusDetail
    {
        get => this._preflightStatusDetail;
        private set => this.SetProperty(ref this._preflightStatusDetail, value);
    }

    /// <summary>Gets the preflight badge label indicating readiness status.</summary>
    public string PreflightBadge => this.PreflightStatusTitle.Contains("Ready", StringComparison.OrdinalIgnoreCase) ? "Preflight ready" : "Preflight pending";

    /// <summary>Gets the run state badge label indicating whether a run is active.</summary>
    public string RunStateBadge => this.IsRunInProgress ? "Run active" : this.RunStatus;

    /// <summary>Gets the badge label showing the total number of persisted runs.</summary>
    public string RunCountBadge => $"{this.RecentRuns.Count} runs";

    /// <summary>Gets the display title of the selected run, or a placeholder when none is selected.</summary>
    public string SelectedRunTitle => this.SelectedRun?.Title ?? "No run selected";

    /// <summary>Gets the detail text for the selected run, or a placeholder when none is selected.</summary>
    public string SelectedRunDetail => this.SelectedRun is null
        ? "Point the shell at a workspace to load persisted run artefacts from .agent-harness/runs."
        : this.SelectedRun.RunDirectory;

    /// <summary>
    /// Initializes the view model by loading workspace history and running preflight validation.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (this._initialized)
        {
            return;
        }

        this._initialized = true;
        await this.RefreshWorkspaceAsync();
        await this.RefreshPreflightAsync();
    }

    /// <summary>
    /// Refreshes the workspace run list, selecting the latest run by default.
    /// </summary>
    public async Task RefreshWorkspaceAsync()
        => await this.RefreshWorkspaceAsync(selectLatestRun: true);

    /// <summary>
    /// Refreshes the workspace run list with optional run selection behavior.
    /// </summary>
    /// <param name="selectLatestRun">When <see langword="true"/>, automatically selects the most recent run.</param>
    public async Task RefreshWorkspaceAsync(bool selectLatestRun)
    {
        string normalizedWorkspace = string.IsNullOrWhiteSpace(this.WorkspacePath)
            ? Environment.CurrentDirectory
            : this.WorkspacePath;

        this.RecentRuns.Clear();
        foreach (RunSummaryViewModel run in this._runHistoryService.GetRecentRuns(normalizedWorkspace))
        {
            this.RecentRuns.Add(run);
        }

        this.RaisePropertyChanged(nameof(this.RunCountBadge));

        if (selectLatestRun && this.RecentRuns.Count > 0)
        {
            await this.SelectRunAsync(this.RecentRuns[0]);
            return;
        }

        if (!selectLatestRun)
        {
            return;
        }

        this.SelectedRun = null;
        this.Artifacts.Clear();
        this.SelectedArtifact = null;
        this.SelectedArtifactPreview = "No runs were found for this workspace yet.";
        TimelineBuilder.ResetForNoRuns(this.TimelineItems, normalizedWorkspace);
        this.RaisePropertyChanged(nameof(this.Headline));
        this.RaisePropertyChanged(nameof(this.Subheadline));
    }

    /// <summary>
    /// Selects a run and loads its artifacts and timeline.
    /// </summary>
    /// <param name="run">The run to select.</param>
    public async Task SelectRunAsync(RunSummaryViewModel run)
        => await this.SelectRunAsync(run, rebuildTimeline: true);

    /// <summary>
    /// Starts an orchestrated run using the current setup configuration.
    /// </summary>
    public async Task StartRunAsync()
    {
        if (this.IsRunInProgress)
        {
            return;
        }

        RunRequest? request = RunRequestFactory.TryBuild(
            this.TaskPrompt, this.WorkspacePath, this.WorkspaceMode, this.Workflow,
            this.ProjectName, this.ModelOverridesText, this.BuildCommand,
            this.PermissionHandlerMode, this.ReviewLoopCodingStyleEnabled,
            this.ReviewLoopSecurityEnabled, this.ReviewLoopArchitectureEnabled,
            this.ArchitectureLoopMode, this.ArchitectureLoopPrompt,
            out string? validationMessage);
        this.SetupValidationMessage = validationMessage ?? string.Empty;
        if (request is null)
        {
            this.RunStatus = "Invalid request";
            return;
        }

        this.IsRunInProgress = true;
        this.RunStatus = "Starting run";
        this.SelectedArtifact = null;
        this.SelectedArtifactPreview = "Artefacts will appear here once the run begins writing output.";
        this.AvailableAgents.Clear();
        this.SessionEvents.Clear();
        this._streamingCoordinator.Transcripts.Clear();

        this.SelectedAgent = null;
        this.SelectedAgentTranscript = "Waiting for agent output...";
        this.TimelineItems.Clear();
        TimelineBuilder.Append(this.TimelineItems, "Run queued", "Desktop host", request.TaskPrompt, TimelineBuilder.AccentForSource("orchestrator"));

        try
        {
            await this.RefreshSetupSummaryAsync(request);
        }
        catch (Exception ex)
        {
            this.SetupSummary = $"Setup summary unavailable: {ex.Message}";
        }

        this._runCts = new CancellationTokenSource();
        Task agentStreamTask = this._streamingCoordinator.ConsumeAgentStreamAsync(this.OnAgentDelta, this._runCts.Token);
        Task sessionEventTask = this._streamingCoordinator.ConsumeSessionEventsAsync(this.OnSessionEvent, this._runCts.Token);
        Progress<RuntimeProgressEvent> progress = new Progress<RuntimeProgressEvent>(evt =>
        {
            string timestamp = evt.TimestampUtc.ToLocalTime().ToString("HH:mm:ss");
            string accent = TimelineBuilder.AccentForSource(evt.Source);
            TimelineBuilder.Append(this.TimelineItems, evt.Source, timestamp, evt.Message, accent);
        });

        try
        {
            RunArtefacts artefacts = await this._runtime.RunAsync(request, progress, cancellationToken: this._runCts.Token);
            this.RunStatus = "Run completed";
            TimelineBuilder.Append(this.TimelineItems, "orchestrator", "Completion", $"Run {artefacts.RunId} completed.", TimelineBuilder.ACCENT_SUCCESS);

            RunSummaryViewModel run = new RunSummaryViewModel(artefacts.RunId, artefacts.RunDirectory);
            await this.RefreshWorkspaceAsync(selectLatestRun: false);
            await this.SelectRunAsync(run, rebuildTimeline: false);
        }
        catch (OperationCanceledException)
        {
            this.RunStatus = "Run canceled";
            TimelineBuilder.Append(this.TimelineItems, "orchestrator", "Canceled", "The current session was canceled from the desktop host.", TimelineBuilder.ACCENT_WARNING);
        }
        catch (Exception ex)
        {
            this.RunStatus = "Run failed";
            TimelineBuilder.Append(this.TimelineItems, "orchestrator", "Failure", ex.Message, TimelineBuilder.ACCENT_DANGER);
        }
        finally
        {
            if (this._runCts is not null)
            {
                await this._runCts.CancelAsync();
                this._runCts.Dispose();
                this._runCts = null;
            }

            try
            {
                await agentStreamTask;
            }
            catch (OperationCanceledException)
            {
                // Expected on run shutdown.
            }

            try
            {
                await sessionEventTask;
            }
            catch (OperationCanceledException)
            {
                // Expected on run shutdown.
            }

            this.IsRunInProgress = false;
            this.RaisePropertyChanged(nameof(this.Headline));
            this.RaisePropertyChanged(nameof(this.Subheadline));
        }
    }

    /// <summary>
    /// Requests cancellation of the active run.
    /// </summary>
    public async Task CancelRunAsync()
    {
        if (this._runCts is null)
        {
            return;
        }

        this.RunStatus = "Canceling run";
        await this._runCts.CancelAsync();
    }

    /// <summary>
    /// Generates a setup summary from the current configuration without starting a run.
    /// </summary>
    public async Task GenerateSetupSummaryAsync()
    {
        RunRequest? request = RunRequestFactory.TryBuild(
            this.TaskPrompt, this.WorkspacePath, this.WorkspaceMode, this.Workflow,
            this.ProjectName, this.ModelOverridesText, this.BuildCommand,
            this.PermissionHandlerMode, this.ReviewLoopCodingStyleEnabled,
            this.ReviewLoopSecurityEnabled, this.ReviewLoopArchitectureEnabled,
            this.ArchitectureLoopMode, this.ArchitectureLoopPrompt,
            out string? validationMessage);
        this.SetupValidationMessage = validationMessage ?? string.Empty;
        if (request is null)
        {
            return;
        }

        await this.RefreshSetupSummaryAsync(request);
    }

    private async Task SelectRunAsync(RunSummaryViewModel run, bool rebuildTimeline)
    {
        this.SelectedRun = run;
        this.Artifacts.Clear();

        IReadOnlyList<ArtifactItemViewModel> artifacts = this._runHistoryService.GetArtifacts(run.RunDirectory);
        foreach (ArtifactItemViewModel artifact in artifacts)
        {
            this.Artifacts.Add(artifact);
        }

        if (this.Artifacts.Count > 0)
        {
            this.SelectArtifact(this.Artifacts[0]);
        }
        else
        {
            this.SelectedArtifact = null;
            this.SelectedArtifactPreview = "This run directory has no readable top-level artefacts yet.";
        }

        if (rebuildTimeline)
        {
            TimelineBuilder.Rebuild(this.TimelineItems, run, artifacts);
        }

        this.RaisePropertyChanged(nameof(this.Headline));
        this.RaisePropertyChanged(nameof(this.Subheadline));
        await Task.CompletedTask;
    }

    /// <summary>
    /// Selects an artifact and updates the preview pane.
    /// </summary>
    /// <param name="artifact">The artifact to select.</param>
    public void SelectArtifact(ArtifactItemViewModel artifact)
    {
        this.SelectedArtifact = artifact;
        this.SelectedArtifactPreview = artifact.Preview;
    }

    private async Task RefreshPreflightAsync()
    {
        PreflightValidationResult result = await this._preflightValidator.ValidateAsync();
        if (result.IsSuccess)
        {
            this.PreflightStatusTitle = "Ready for host integration";
            this.PreflightStatusDetail = "Copilot CLI preflight succeeded. The desktop shell can now grow into a full live-run host on top of the shared runtime services.";
            return;
        }

        this.PreflightStatusTitle = "Preflight requires attention";
        this.PreflightStatusDetail = result.FixSteps.Count == 0
            ? result.Summary
            : string.Join(Environment.NewLine, result.FixSteps);
    }

    private async Task RefreshSetupSummaryAsync(RunRequest request)
    {
        this.RunStatus = this.IsRunInProgress ? this.RunStatus : "Generating summary";
        this.SetupSummary = await this._summaryGenerator.GenerateSetupSummaryAsync(request, CancellationToken.None);
        if (!this.IsRunInProgress)
        {
            this.RunStatus = "Idle";
        }
    }

    private void OnAgentDelta(AgentStreamDeltaEvent evt)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            AgentItemViewModel? agent = this.AvailableAgents.FirstOrDefault(item => item.AgentId == evt.AgentId);
            if (agent is null)
            {
                agent = new AgentItemViewModel(evt.AgentId, evt.AgentRole);
                this.AvailableAgents.Add(agent);
            }

            if (this.SelectedAgent is null)
            {
                this.SelectedAgent = agent;
            }
            else if (this.SelectedAgent.AgentId == evt.AgentId)
            {
                this.RefreshSelectedAgentTranscript();
            }
        });
    }

    private void OnSessionEvent(CopilotSessionLifecycleEvent evt)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            string formattedTime = evt.TimestampUtc.ToLocalTime().ToString("HH:mm:ss");
            string detail = evt.Details ?? "No additional details.";
            SessionEventItemViewModel sessionEvent = new SessionEventItemViewModel(
                evt.EventType,
                formattedTime,
                evt.Model,
                evt.SessionId,
                detail);
            this.SessionEvents.Add(sessionEvent);

            if (this.SessionEvents.Count > 100)
            {
                this.SessionEvents.RemoveAt(0);
            }
        });
    }

    private void RefreshSelectedAgentTranscript()
    {
        if (this.SelectedAgent is null)
        {
            this.SelectedAgentTranscript = "Run a session to stream agent output here.";
            return;
        }

        if (this._streamingCoordinator is null)
        {
            return;
        }

        string? transcript = this._streamingCoordinator.Transcripts.GetTranscript(this.SelectedAgent.AgentId);
        this.SelectedAgentTranscript = transcript ?? "Waiting for output from the selected agent.";
    }

}