using System.Collections.ObjectModel;
using System.Text;
using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using Microsoft.Extensions.Options;

namespace ArchHarness.Desktop.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private const string DEFAULT_TASK_PROMPT = "Implement requested change";
    private const string DEFAULT_ARCH_LOOP_TASK_PROMPT = "Run coding style, security, and architecture review loop for the existing workspace and apply required remediation.";
    private const string APPROVE_ALL = "approve-all";
    private const string PROMPT = "prompt";

    private readonly IRunHistoryService _runHistoryService;
    private readonly OrchestratorRuntime _runtime;
    private readonly IAgentStreamEventStream _agentStreamEventStream;
    private readonly ICopilotSessionEventStream _sessionEventStream;
    private readonly IStartupPreflightValidator _preflightValidator;
    private readonly SetupSummaryGenerator _summaryGenerator;
    private readonly Dictionary<string, StringBuilder> _agentTranscripts = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
    private readonly object _agentSync = new object();
    private string _workspacePath = Environment.CurrentDirectory;
    private string _taskPrompt = DEFAULT_TASK_PROMPT;
    private string _workflow = "auto";
    private string _workspaceMode = "existing-folder";
    private string _permissionHandlerMode = APPROVE_ALL;
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
        this._agentStreamEventStream = null!;
        this._sessionEventStream = null!;
        this._preflightValidator = null!;
        this._summaryGenerator = null!;
        this.RecentRuns = new ObservableCollection<RunSummaryViewModel>();
        this.Artifacts = new ObservableCollection<ArtifactItemViewModel>();
        this.TimelineItems = new ObservableCollection<TimelineItemViewModel>();
        this.AvailableAgents = new ObservableCollection<AgentItemViewModel>();
        this.SessionEvents = new ObservableCollection<SessionEventItemViewModel>();
        this.WorkspaceModes = new[] { "existing-folder", "new-project", "existing-git" };
        this.PermissionModes = new[] { APPROVE_ALL, PROMPT };
        this._taskPrompt = DEFAULT_TASK_PROMPT;
        this._workflow = "auto";
        this._setupSummary = "Design-time preview";
        this.SeedEmptyTimeline();
    }
    private CancellationTokenSource? _runCts;

    public MainWindowViewModel(
        IRunHistoryService runHistoryService,
        OrchestratorRuntime runtime,
        IAgentStreamEventStream agentStreamEventStream,
        ICopilotSessionEventStream sessionEventStream,
        IStartupPreflightValidator preflightValidator,
        SetupSummaryGenerator summaryGenerator,
        IOptions<AgentsOptions> agentsOptions)
    {
        this._runHistoryService = runHistoryService;
        this._runtime = runtime;
        this._agentStreamEventStream = agentStreamEventStream;
        this._sessionEventStream = sessionEventStream;
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
        this.PermissionModes = new[] { APPROVE_ALL, PROMPT };
        this.SeedEmptyTimeline();
    }

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

    public ObservableCollection<RunSummaryViewModel> RecentRuns { get; }

    public ObservableCollection<ArtifactItemViewModel> Artifacts { get; }

    public ObservableCollection<TimelineItemViewModel> TimelineItems { get; }

    public ObservableCollection<AgentItemViewModel> AvailableAgents { get; }

    public ObservableCollection<SessionEventItemViewModel> SessionEvents { get; }

    public IReadOnlyList<string> WorkspaceModes { get; }

    public IReadOnlyList<string> PermissionModes { get; }

    public string WorkspacePath
    {
        get => this._workspacePath;
        set => this.SetProperty(ref this._workspacePath, value);
    }

    public string TaskPrompt
    {
        get => this._taskPrompt;
        set => this.SetProperty(ref this._taskPrompt, value);
    }

    public string Workflow
    {
        get => this._workflow;
        set => this.SetProperty(ref this._workflow, value);
    }

    public string WorkspaceMode
    {
        get => this._workspaceMode;
        set => this.SetProperty(ref this._workspaceMode, value);
    }

    public string PermissionHandlerMode
    {
        get => this._permissionHandlerMode;
        set => this.SetProperty(ref this._permissionHandlerMode, NormalizePermissionMode(value));
    }

    public string ProjectName
    {
        get => this._projectName;
        set => this.SetProperty(ref this._projectName, value);
    }

    public string ModelOverridesText
    {
        get => this._modelOverridesText;
        set => this.SetProperty(ref this._modelOverridesText, value);
    }

    public string BuildCommand
    {
        get => this._buildCommand;
        set => this.SetProperty(ref this._buildCommand, value);
    }

    public string ArchitectureLoopPrompt
    {
        get => this._architectureLoopPrompt;
        set => this.SetProperty(ref this._architectureLoopPrompt, value);
    }

    public bool ReviewLoopCodingStyleEnabled
    {
        get => this._reviewLoopCodingStyleEnabled;
        set => this.SetProperty(ref this._reviewLoopCodingStyleEnabled, value);
    }

    public bool ReviewLoopSecurityEnabled
    {
        get => this._reviewLoopSecurityEnabled;
        set => this.SetProperty(ref this._reviewLoopSecurityEnabled, value);
    }

    public bool ReviewLoopArchitectureEnabled
    {
        get => this._reviewLoopArchitectureEnabled;
        set => this.SetProperty(ref this._reviewLoopArchitectureEnabled, value);
    }

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

    public bool CanStartRun => !this.IsRunInProgress;

    public bool CanCancelRun => this.IsRunInProgress;

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

    public string SetupSummary
    {
        get => this._setupSummary;
        private set => this.SetProperty(ref this._setupSummary, value);
    }

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

    public bool HasSetupValidationMessage => !string.IsNullOrWhiteSpace(this.SetupValidationMessage);

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

    public ArtifactItemViewModel? SelectedArtifact
    {
        get => this._selectedArtifact;
        private set => this.SetProperty(ref this._selectedArtifact, value);
    }

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

    public string SelectedArtifactPreview
    {
        get => this._selectedArtifactPreview;
        private set => this.SetProperty(ref this._selectedArtifactPreview, value);
    }

    public string SelectedAgentTranscript
    {
        get => this._selectedAgentTranscript;
        private set => this.SetProperty(ref this._selectedAgentTranscript, value);
    }

    public string Headline => this.SelectedRun is null ? "Desktop run inspector" : this.SelectedRun.Title;

    public string Subheadline => this.IsRunInProgress
        ? "Live runtime progress, agent streaming output, and artefact generation are active in the desktop host."
        : this.SelectedRun is null
            ? "The desktop host can now launch runs, stream progress, and inspect persisted sessions from the same shell."
            : "Inspect persisted run artefacts or start a new orchestrated session from the setup panel.";

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

    public string PreflightStatusDetail
    {
        get => this._preflightStatusDetail;
        private set => this.SetProperty(ref this._preflightStatusDetail, value);
    }

    public string PreflightBadge => this.PreflightStatusTitle.Contains("Ready", StringComparison.OrdinalIgnoreCase) ? "Preflight ready" : "Preflight pending";

    public string RunStateBadge => this.IsRunInProgress ? "Run active" : this.RunStatus;

    public string RunCountBadge => $"{this.RecentRuns.Count} runs";

    public string SelectedRunTitle => this.SelectedRun?.Title ?? "No run selected";

    public string SelectedRunDetail => this.SelectedRun is null
        ? "Point the shell at a workspace to load persisted run artefacts from .agent-harness/runs."
        : this.SelectedRun.RunDirectory;

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

    public async Task RefreshWorkspaceAsync()
        => await this.RefreshWorkspaceAsync(selectLatestRun: true);

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
            this.RaisePropertyChanged(nameof(this.RunCountBadge));
            return;
        }

        this.SelectedRun = null;
        this.Artifacts.Clear();
        this.SelectedArtifact = null;
        this.SelectedArtifactPreview = "No runs were found for this workspace yet.";
        this.ResetTimelineForNoRuns(normalizedWorkspace);
        this.RaisePropertyChanged(nameof(this.Headline));
        this.RaisePropertyChanged(nameof(this.Subheadline));
    }

    public async Task SelectRunAsync(RunSummaryViewModel run)
        => await this.SelectRunAsync(run, rebuildTimeline: true);

    public async Task StartRunAsync()
    {
        if (this.IsRunInProgress)
        {
            return;
        }

        RunRequest? request = this.TryBuildRunRequest(out string? validationMessage);
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
        lock (this._agentSync)
        {
            this._agentTranscripts.Clear();
        }

        this.SelectedAgent = null;
        this.SelectedAgentTranscript = "Waiting for agent output...";
        this.TimelineItems.Clear();
        this.AppendTimelineItem("Run queued", "Desktop host", request.TaskPrompt, "#F16436");

        try
        {
            await this.RefreshSetupSummaryAsync(request);
        }
        catch (Exception ex)
        {
            this.SetupSummary = $"Setup summary unavailable: {ex.Message}";
        }

        this._runCts = new CancellationTokenSource();
        Task agentStreamTask = this.ConsumeAgentStreamAsync(this._runCts.Token);
        Task sessionEventTask = this.ConsumeSessionEventsAsync(this._runCts.Token);
        Progress<RuntimeProgressEvent> progress = new Progress<RuntimeProgressEvent>(evt =>
            this.AppendTimelineItem(evt.Source, evt.TimestampUtc.ToLocalTime().ToString("HH:mm:ss"), evt.Message, AccentForSource(evt.Source)));

        try
        {
            RunArtefacts artefacts = await this._runtime.RunAsync(request, progress, this._runCts.Token);
            this.RunStatus = "Run completed";
            this.AppendTimelineItem("orchestrator", "Completion", $"Run {artefacts.RunId} completed.", "#5FD08C");

            RunSummaryViewModel run = new RunSummaryViewModel(artefacts.RunId, artefacts.RunDirectory);
            await this.RefreshWorkspaceAsync(selectLatestRun: false);
            await this.SelectRunAsync(run, rebuildTimeline: false);
        }
        catch (OperationCanceledException)
        {
            this.RunStatus = "Run canceled";
            this.AppendTimelineItem("orchestrator", "Canceled", "The current session was canceled from the desktop host.", "#FFB347");
        }
        catch (Exception ex)
        {
            this.RunStatus = "Run failed";
            this.AppendTimelineItem("orchestrator", "Failure", ex.Message, "#FF6B6B");
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

    public async Task CancelRunAsync()
    {
        if (this._runCts is null)
        {
            return;
        }

        this.RunStatus = "Canceling run";
        await this._runCts.CancelAsync();
    }

    public async Task GenerateSetupSummaryAsync()
    {
        RunRequest? request = this.TryBuildRunRequest(out string? validationMessage);
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
            this.RebuildTimeline(run, artifacts);
        }

        this.RaisePropertyChanged(nameof(this.Headline));
        this.RaisePropertyChanged(nameof(this.Subheadline));
        await Task.CompletedTask;
    }

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

    private void SeedEmptyTimeline()
    {
        this.TimelineItems.Clear();
        this.TimelineItems.Add(new TimelineItemViewModel(
            "Desktop runtime ready",
            "Foundation milestone",
            "Console and desktop hosts now share the same runtime registration path, and the desktop host can start orchestrated runs directly.",
            "#F16436"));
    }

    private void ResetTimelineForNoRuns(string workspacePath)
    {
        this.TimelineItems.Clear();
        this.TimelineItems.Add(new TimelineItemViewModel(
            "No persisted runs",
            "Workspace scan",
            $"Nothing was found under {Path.Combine(Path.GetFullPath(workspacePath), ".agent-harness", "runs")}",
            "#5AA7FF"));
        this.TimelineItems.Add(new TimelineItemViewModel(
            "Next desktop milestone",
            "Live session hosting",
            "The shell is ready to ingest preflight status and stored run data while live run execution moves out of the console host.",
            "#F16436"));
    }

    private void RebuildTimeline(RunSummaryViewModel run, IReadOnlyList<ArtifactItemViewModel> artifacts)
    {
        this.TimelineItems.Clear();
        this.TimelineItems.Add(new TimelineItemViewModel(
            run.Title,
            "Selected session",
            run.RunDirectory,
            "#F16436"));
        this.TimelineItems.Add(new TimelineItemViewModel(
            "Artefacts indexed",
            $"{artifacts.Count} files discovered",
            artifacts.Count == 0 ? "No top-level files were found for this run." : string.Join(", ", artifacts.Take(6).Select(a => a.Name)),
            "#5AA7FF"));
        this.TimelineItems.Add(new TimelineItemViewModel(
            "Desktop adaptation",
            "Reference-inspired layout",
            "The left rail, timeline surface, and detail pane now map to ArchHarness runs, live status, and artefacts instead of terminal screens.",
            "#5FD08C"));
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

    private async Task ConsumeAgentStreamAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (AgentStreamDeltaEvent evt in this._agentStreamEventStream.ReadAllAsync(cancellationToken))
            {
                lock (this._agentSync)
                {
                    if (!this._agentTranscripts.TryGetValue(evt.AgentId, out StringBuilder? transcript))
                    {
                        transcript = new StringBuilder();
                        this._agentTranscripts[evt.AgentId] = transcript;
                    }

                    transcript.Append(evt.DeltaContent);
                }

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
        }
        catch (OperationCanceledException)
        {
            // Expected on run shutdown.
        }
    }

    private async Task ConsumeSessionEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (CopilotSessionLifecycleEvent evt in this._sessionEventStream.ReadAllAsync(cancellationToken))
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    this.SessionEvents.Add(new SessionEventItemViewModel(
                        evt.EventType,
                        evt.TimestampUtc.ToLocalTime().ToString("HH:mm:ss"),
                        evt.Model,
                        evt.SessionId,
                        evt.Details ?? "No additional details."));

                    if (this.SessionEvents.Count > 100)
                    {
                        this.SessionEvents.RemoveAt(0);
                    }
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on run shutdown.
        }
    }

    private RunRequest? TryBuildRunRequest(out string? validationMessage)
    {
        validationMessage = null;
        string taskPrompt = this.ArchitectureLoopMode
            ? string.IsNullOrWhiteSpace(this.TaskPrompt) ? DEFAULT_ARCH_LOOP_TASK_PROMPT : this.TaskPrompt.Trim()
            : string.IsNullOrWhiteSpace(this.TaskPrompt) ? string.Empty : this.TaskPrompt.Trim();

        if (string.IsNullOrWhiteSpace(taskPrompt))
        {
            validationMessage = "Task prompt is required unless architecture loop mode is using its default task.";
            return null;
        }

        string workspacePath = string.IsNullOrWhiteSpace(this.WorkspacePath) ? Environment.CurrentDirectory : this.WorkspacePath.Trim();
        string workspaceMode = string.IsNullOrWhiteSpace(this.WorkspaceMode) ? "existing-folder" : this.WorkspaceMode;
        string workflow = this.ArchitectureLoopMode
            ? "architecture-loop"
            : string.IsNullOrWhiteSpace(this.Workflow) ? "auto" : this.Workflow.Trim();

        return new RunRequest(
            TaskPrompt: taskPrompt,
            WorkspacePath: workspacePath,
            WorkspaceMode: workspaceMode,
            Workflow: workflow,
            ProjectName: string.IsNullOrWhiteSpace(this.ProjectName) ? null : this.ProjectName.Trim(),
            ModelOverrides: ParseOverrides(this.ModelOverridesText),
            BuildCommand: string.IsNullOrWhiteSpace(this.BuildCommand) ? null : this.BuildCommand.Trim(),
            PermissionHandlerMode: NormalizePermissionMode(this.PermissionHandlerMode),
            ReviewLoopAgents: new ReviewLoopAgentSelection(
                this.ReviewLoopCodingStyleEnabled,
                this.ReviewLoopSecurityEnabled,
                this.ReviewLoopArchitectureEnabled),
            ArchitectureLoopMode: this.ArchitectureLoopMode,
            ArchitectureLoopPrompt: string.IsNullOrWhiteSpace(this.ArchitectureLoopPrompt) ? null : this.ArchitectureLoopPrompt.Trim());
    }

    private void AppendTimelineItem(string title, string subtitle, string detail, string accent)
    {
        this.TimelineItems.Add(new TimelineItemViewModel(title, subtitle, detail, accent));
    }

    private void RefreshSelectedAgentTranscript()
    {
        if (this.SelectedAgent is null)
        {
            this.SelectedAgentTranscript = "Run a session to stream agent output here.";
            return;
        }

        lock (this._agentSync)
        {
            if (!this._agentTranscripts.TryGetValue(this.SelectedAgent.AgentId, out StringBuilder? transcript))
            {
                this.SelectedAgentTranscript = "Waiting for output from the selected agent.";
                return;
            }

            this.SelectedAgentTranscript = transcript.ToString();
        }
    }

    private static string AccentForSource(string source)
        => source.ToLowerInvariant() switch
        {
            "orchestrator" => "#F16436",
            "build" => "#5AA7FF",
            "security" => "#FF6B6B",
            "architecture" => "#5FD08C",
            "codingstyle" => "#F5C451",
            _ => "#AAB6C4"
        };

    private static IDictionary<string, string>? ParseOverrides(string? overrideText)
    {
        if (string.IsNullOrWhiteSpace(overrideText))
        {
            return null;
        }

        Dictionary<string, string> output = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string[] segments = overrideText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string segment in segments)
        {
            int idx = segment.IndexOf('=');
            if (idx <= 0 || idx == segment.Length - 1)
            {
                continue;
            }

            string role = segment[..idx].Trim();
            string model = segment[(idx + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(role) && !string.IsNullOrWhiteSpace(model))
            {
                output[role] = model;
            }
        }

        return output.Count == 0 ? null : output;
    }

    private static string NormalizePermissionMode(string? mode)
        => string.Equals(mode, PROMPT, StringComparison.OrdinalIgnoreCase) ? PROMPT : APPROVE_ALL;
}