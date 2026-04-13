export const state = {
  bootstrap: null,
  settings: null,
  models: [],
  projects: [],
  activeProjectId: null,
  activeRunId: null,
  mainPanelView: "stream",
  activeRun: null,
  artifacts: [],
  selectedRunState: null,
  selectedArtifactPath: null,
  streamSections: {},
  streamOrder: [],
  streamAutoScroll: true,
  agentSpinningUp: {},
  eventSource: null,
  pendingInteraction: null,
  pendingInteractionSignature: null,
  dismissedPendingInteractionSignature: null,
  pendingInteractionDraft: "",
  pendingInteractionDrafts: {},
  pendingPlanRevisionDraft: "",
  interactionPollHandle: null,
  pendingInteractionAbortController: null,
  pendingInteractionInFlight: false,
  isUnloading: false,
  openModalId: null,
  expandedProjectIds: new Set(),
  seenRunIds: new Set(),
  providers: [],
  projectBranchInfoById: {},
  projectBranchRequestsInFlight: new Set(),
  selectedRunLoadToken: 0,
  branchMenuOpen: false,
  selectedReviewLoopAgents: null,
  gitChangeReview: {
    projectId: null,
    currentBranch: null,
    targetBranch: null,
    files: [],
    selectedPath: null,
    diffByPath: {},
    loading: false,
    diffLoadingPath: null,
    error: ""
  },
  branchChanges: {
    projectId: null,
    currentBranch: null,
    files: [],
    selectedPath: null,
    diffByPath: {},
    loading: false,
    diffLoadingPath: null,
    error: "",
    requestToken: 0
  },
  composerMenuOpen: null,
  branchSwitchProjectId: null,
  providerConnectionTested: false,
  editingProviderName: null,
  editingProviderStorageMode: 0,
  editingProviderHasStoredToken: false,
  editingProviderGitHubAuthenticationMode: 0,
  editingProviderGitHubAuthenticatedUser: null,
  providerClearStoredToken: false,
  providerGitHubOAuthFlow: null,
  providerGitHubOAuthToken: null,
  providerGitHubOAuthPollHandle: null
};

export const elements = {
  sidebar: document.getElementById("sidebar"),
  newProjectButton: document.getElementById("new-project-button"),
  settingsButton: document.getElementById("settings-button"),
  projectList: document.getElementById("project-list"),
  workspaceTitle: document.getElementById("workspace-title"),
  workspaceBranchWrap: document.getElementById("workspace-branch-wrap"),
  workspaceBranchButton: document.getElementById("workspace-branch-button"),
  workspaceBranchLabel: document.getElementById("workspace-branch-label"),
  workspaceBranchMenu: document.getElementById("workspace-branch-menu"),
  eventStreamState: null,
  streamSummary: null,
  streamToolbar: document.getElementById("stream-toolbar"),
  streamView: document.getElementById("stream-view"),
  streamViewButton: document.getElementById("stream-view-button"),
  branchChangesViewButton: document.getElementById("branch-changes-view-button"),
  branchChangesView: document.getElementById("branch-changes-view"),
  branchChangesTitle: document.getElementById("branch-changes-title"),
  branchChangesSummary: document.getElementById("branch-changes-summary"),
  branchChangesRefresh: document.getElementById("branch-changes-refresh"),
  branchChangeList: document.getElementById("branch-change-list"),
  branchDiffMeta: document.getElementById("branch-diff-meta"),
  branchDiffPreview: document.getElementById("branch-diff-preview"),
  streamEmpty: document.getElementById("stream-empty"),
  streamSections: document.getElementById("stream-sections"),
  inlineInteraction: document.getElementById("inline-interaction"),

  taskPrompt: document.getElementById("task-prompt"),
  runModeWrap: document.getElementById("run-mode-wrap"),
  runModeButton: document.getElementById("run-mode-button"),
  runModeLabel: document.getElementById("run-mode-label"),
  runModeMenu: document.getElementById("run-mode-menu"),
  runMode: document.getElementById("run-mode"),
  permissionModeWrap: document.getElementById("permission-mode-wrap"),
  permissionModeButton: document.getElementById("permission-mode-button"),
  permissionModeLabel: document.getElementById("permission-mode-label"),
  permissionModeMenu: document.getElementById("permission-mode-menu"),
  permissionMode: document.getElementById("permission-mode"),
  architectureReviewChip: document.getElementById("architecture-review-chip"),
  architectureReviewPresetButton: document.getElementById("architecture-review-preset-button"),
  architectureReviewPresetLabel: document.getElementById("architecture-review-preset-label"),
  architectureReviewPresetMenu: document.getElementById("architecture-review-preset-menu"),
  architectureReviewPreset: document.getElementById("architecture-review-preset"),
  architectureReviewAgentsWrap: document.getElementById("architecture-review-agents-wrap"),
  architectureReviewAgentsButton: document.getElementById("architecture-review-agents-button"),
  architectureReviewAgentsLabel: document.getElementById("architecture-review-agents-label"),
  architectureReviewAgentsMenu: document.getElementById("architecture-review-agents-menu"),
  startRun: document.getElementById("start-run"),
  pauseRun: document.getElementById("pause-run"),
  cancelRun: document.getElementById("cancel-run"),
  implementRun: document.getElementById("implement-run-button"),
  modalBackdrop: document.getElementById("modal-backdrop"),
  newProjectModal: document.getElementById("new-project-modal"),
  newProjectForm: document.getElementById("new-project-form"),
  newProjectName: document.getElementById("new-project-name"),
  newProjectPath: document.getElementById("new-project-path"),
  pickProjectFolder: document.getElementById("pick-project-folder"),
  reviewPrPickFolder: document.getElementById("review-pr-pick-folder"),
  reviewPrGoButton: document.getElementById("review-pr-go-button"),
  newProjectPermission: document.getElementById("new-project-permission"),
  newProjectArchitecture: document.getElementById("new-project-architecture"),
  newProjectArchitecturePrompt: document.getElementById("new-project-architecture-prompt"),
  projectPickerNote: document.getElementById("project-picker-note"),
  settingsModal: document.getElementById("settings-modal"),
  settingsForm: document.getElementById("settings-form"),
  settingsGrid: document.getElementById("settings-grid"),
  settingsPermissionMode: document.getElementById("settings-permission-mode"),
  settingsArchitectureMode: document.getElementById("settings-architecture-mode"),
  settingsArchitecturePrompt: document.getElementById("settings-architecture-prompt"),
  runDetailsModal: document.getElementById("run-details-modal"),
  runDetailsTitle: document.getElementById("run-details-title"),
  resumeRun: document.getElementById("resume-run-button"),
  artifactList: document.getElementById("artifact-list"),
  artifactPreview: document.getElementById("artifact-preview"),
  artifactSummary: document.getElementById("artifact-summary"),
  gitChangesModal: document.getElementById("git-changes-modal"),
  gitChangesTitle: document.getElementById("git-changes-title"),
  gitChangesSummary: document.getElementById("git-changes-summary"),
  gitChangeList: document.getElementById("git-change-list"),
  gitDiffMeta: document.getElementById("git-diff-meta"),
  gitDiffPreview: document.getElementById("git-diff-preview"),
  gitChangesActionStatus: document.getElementById("git-changes-action-status"),
  gitChangesStashButton: document.getElementById("git-changes-stash-button"),
  gitChangesCloseButton: document.getElementById("git-changes-close-button"),
  projectTemplate: document.getElementById("project-template"),
  runTemplate: document.getElementById("run-template"),
  artifactTemplate: document.getElementById("artifact-template"),
  providerList: document.getElementById("provider-list"),
  providerSetup: document.getElementById("provider-setup"),
  btnAddProvider: document.getElementById("btn-add-provider"),
  btnTestProvider: document.getElementById("btn-test-provider"),
  btnSaveProvider: document.getElementById("btn-save-provider"),
  btnCancelProvider: document.getElementById("btn-cancel-provider"),
  providerTestStatus: document.getElementById("provider-test-status"),
  providerTypeRadios: document.querySelectorAll('input[name="pf-type"]'),
  providerDisplayName: document.getElementById("pf-display-name"),
  providerServerUrlWrap: document.getElementById("pf-server-url-wrap"),
  providerServerUrl: document.getElementById("pf-server-url"),
  providerOrgWrap: document.getElementById("pf-org-wrap"),
  providerOrgLabel: document.getElementById("pf-org-label"),
  providerOrg: document.getElementById("pf-org"),
  providerGitHubOwnerTypeWrap: document.getElementById("pf-github-owner-type-wrap"),
  providerGitHubOwnerType: document.getElementById("pf-github-owner-type"),
  providerPatWrap: document.getElementById("pf-pat-wrap"),
  providerPat: document.getElementById("pf-pat"),
  providerPatHint: document.getElementById("pf-pat-hint"),
  providerPatClear: document.getElementById("pf-clear-token"),
  providerPatClearNote: document.getElementById("pf-clear-token-note"),
  providerPatToggle: document.getElementById("pf-pat-toggle"),
  providerPatToggleIcon: document.getElementById("pf-pat-toggle-icon"),
  providerGitHubOAuthWrap: document.getElementById("pf-github-oauth-wrap"),
  providerGitHubOAuthStart: document.getElementById("pf-github-oauth-start"),
  providerGitHubOAuthLink: document.getElementById("pf-github-oauth-link"),
  providerGitHubOAuthCodeRow: document.getElementById("pf-github-oauth-code-row"),
  providerGitHubOAuthCode: document.getElementById("pf-github-oauth-code"),
  providerGitHubOAuthCopy: document.getElementById("pf-github-oauth-copy"),
  providerGitHubOAuthCopyNote: document.getElementById("pf-github-oauth-copy-note"),
  providerGitHubOAuthHint: document.getElementById("pf-github-oauth-hint")
};

export function getActiveProject() {
  return state.projects.find(project => project.projectId === state.activeProjectId) || null;
}

export function getProjectById(projectId) {
  return state.projects.find(project => project.projectId === projectId) || null;
}

export function getSelectedRun(project = getActiveProject()) {
  if (!project || !Array.isArray(project.runs)) {
    return null;
  }

  return project.runs.find(run => run.runId === state.activeRunId) || null;
}

export function isSelectedRunLive() {
  return !!state.activeRun?.isRunning && state.activeRun?.runId === state.activeRunId;
}

export function getSelectedProjectAndRun() {
  const project = state.projects.find(candidate => candidate.projectId === state.activeProjectId) || null;
  if (!project) {
    return { project: null, run: null };
  }

  const run = Array.isArray(project.runs)
    ? project.runs.find(candidate => candidate.runId === state.activeRunId) || null
    : null;
  return { project, run };
}

export function getProjectRunCount(project) {
  return Array.isArray(project?.runs) ? project.runs.length : 0;
}

export function toProjectBranchInfo(branchInfo) {
  if (!branchInfo) {
    return null;
  }

  return {
    isGitRepository: !!branchInfo.isGitRepository,
    currentBranch: branchInfo.currentBranch || null,
    branches: Array.isArray(branchInfo.branches) ? branchInfo.branches : []
  };
}

export function applyProjectBranchInfo(projectId, branchInfo) {
  if (!projectId || !branchInfo) {
    return;
  }

  state.projectBranchInfoById[projectId] = toProjectBranchInfo(branchInfo);
}
