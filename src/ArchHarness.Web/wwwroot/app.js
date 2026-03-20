const state = {
  bootstrap: null,
  settings: null,
  models: [],
  projects: [],
  activeProjectId: null,
  activeRunId: null,
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
  composerMenuOpen: null,
  branchSwitchProjectId: null,
  providerConnectionTested: false,
  editingProviderName: null,
  editingProviderStorageMode: 0
};

let reviewPrState = {
  step: 0,
  providers: [],
  selectedProvider: null,
  autoSelectedProvider: false,
  allPullRequests: [],
  pullRequests: [],
  selectedProjects: [],
  selectedRepositories: [],
  selectedAuthors: [],
  pullRequestStreamController: null,
  isPullRequestStreamLoading: false,
  isPullRequestStreamComplete: false,
  pullRequestError: "",
  selectedPr: null,
  projectId: null,
  folderPath: '',
  prFiles: [],
  prFilesError: "",
  isPreparingWorkspace: false,
  isStartingReview: false
};

const desktopBridge = globalThis.archHarnessDesktop || null;

function setDesktopInset(name, value) {
  document.documentElement.style.setProperty(name, `${Math.max(0, Math.ceil(value))}px`);
}

function applyDesktopChrome() {
  const root = document.documentElement;
  const chrome = desktopBridge?.chrome || null;
  if (!chrome) {
    return;
  }

  root.dataset.desktopPlatform = chrome.platform;

  if (!chrome.titleBarOverlay) {
    delete root.dataset.titleBarOverlay;
    return;
  }

  root.dataset.titleBarOverlay = "true";

  const overlay = navigator.windowControlsOverlay;
  const syncOverlayInsets = () => {
    let rightInset = 150;
    let topbarHeight = 46;

    if (overlay?.visible && typeof overlay.getTitlebarAreaRect === "function") {
      const rect = overlay.getTitlebarAreaRect();
      if (rect && Number.isFinite(rect.x) && Number.isFinite(rect.width)) {
        rightInset = Math.max(150, globalThis.innerWidth - (rect.x + rect.width));
      }

      if (rect && Number.isFinite(rect.height)) {
        topbarHeight = Math.max(46, rect.height);
      }
    }

    setDesktopInset("--desktop-right-inset", rightInset);
    setDesktopInset("--desktop-titlebar-height", topbarHeight);
  };

  syncOverlayInsets();
  overlay?.addEventListener?.("geometrychange", syncOverlayInsets);
  globalThis.addEventListener("resize", syncOverlayInsets);
}

const STORAGE_KEY = "archharness.web.shell-state";
const IDLE_INTERACTION_POLL_MS = 5000;
const ACTIVE_INTERACTION_POLL_MS = 400;
const STREAM_RENDER_DELAY_MS = 140;
const DEFAULT_STREAM_EMPTY_MESSAGE = "Start a run from the composer to stream orchestrator, agent, and subagent output here.";
const REVIEW_PROVIDER_NAME_MAX_LENGTH = 128;
const REVIEW_FILTER_MAX_LENGTH = 200;
const REVIEW_PULL_REQUEST_ID_MAX_LENGTH = 20;
const PAT_STORAGE_MODE_PROTECTED = 0;
const PAT_STORAGE_MODE_PLAINTEXT = 1;
const LEGACY_AUTOFILL_PROMPTS = new Set([
  "Implement requested change",
  "Run coding style, security, and architecture review loop for the existing workspace and apply required remediation."
]);
const ROLE_LABELS = {
  conversation: "Conversation",
  orchestration: "Orchestration",
  frontendDeveloper: "Frontend Developer",
  backendDeveloper: "Backend Developer",
  build: "Build",
  codingStyle: "Coding Style",
  security: "Security",
  architecture: "Architecture"
};

const elements = {
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
  startRun: document.getElementById("start-run"),
  cancelRun: document.getElementById("cancel-run"),
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
  providerPatToggle: document.getElementById("pf-pat-toggle"),
  providerPatToggleIcon: document.getElementById("pf-pat-toggle-icon")
};

async function requestJson(url, options) {
  const response = await fetch(url, options);
  if (response.status === 204) {
    return null;
  }

  if (!response.ok) {
    const contentType = response.headers.get("content-type") || "";
    let errorData = null;
    let text = "";

    if (contentType.includes("application/json")) {
      errorData = await response.json();
      text = errorData?.error || errorData?.title || JSON.stringify(errorData);
    } else {
      text = await response.text();
    }

    const error = new Error(text || `Request failed with status ${response.status}`);
    error.status = response.status;
    error.data = errorData;
    throw error;
  }

  return response.json();
}

async function requestEventStream(url, options) {
  const response = await fetch(url, options);
  if (!response.ok) {
    const contentType = response.headers.get("content-type") || "";
    let errorData = null;
    let text = "";

    if (contentType.includes("application/json")) {
      errorData = await response.json();
      text = errorData?.error || errorData?.title || JSON.stringify(errorData);
    } else {
      text = await response.text();
    }

    const error = new Error(text || `Request failed with status ${response.status}`);
    error.status = response.status;
    error.data = errorData;
    throw error;
  }

  if (!response.body) {
    return;
  }

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";

  const processBlock = block => {
    const normalized = block.replaceAll("\r", "");
    if (!normalized.trim()) {
      return;
    }

    let eventName = "message";
    const dataLines = [];
    normalized.split("\n").forEach(line => {
      if (!line || line.startsWith(":")) {
        return;
      }

      if (line.startsWith("event:")) {
        eventName = line.slice("event:".length).trim() || "message";
        return;
      }

      if (line.startsWith("data:")) {
        dataLines.push(line.slice("data:".length).trimStart());
      }
    });

    let data = null;
    const serialized = dataLines.join("\n");
    if (serialized) {
      try {
        data = JSON.parse(serialized);
      } catch {
        data = serialized;
      }
    }

    options?.onEvent?.({ event: eventName, data });
  };

  const flushBuffer = finalChunk => {
    let delimiterIndex = buffer.indexOf("\n\n");
    while (delimiterIndex >= 0) {
      processBlock(buffer.slice(0, delimiterIndex));
      buffer = buffer.slice(delimiterIndex + 2);
      delimiterIndex = buffer.indexOf("\n\n");
    }

    if (finalChunk && buffer.trim()) {
      processBlock(buffer);
      buffer = "";
    }
  };

  while (true) {
    const { value, done } = await reader.read();
    if (done) {
      buffer += decoder.decode();
      flushBuffer(true);
      break;
    }

    buffer += decoder.decode(value, { stream: true });
    flushBuffer(false);
  }
}

function saveShellState() {
  const payload = {
    activeProjectId: state.activeProjectId,
    activeRunId: state.activeRunId,
    taskPrompt: elements.taskPrompt.value,
    runMode: elements.runMode.value,
    permissionMode: elements.permissionMode.value,
    architectureReviewPreset: elements.architectureReviewPreset.value,
    seenRunIds: [...state.seenRunIds]
  };
  globalThis.localStorage.setItem(STORAGE_KEY, JSON.stringify(payload));
}

function restoreShellState() {
  const raw = globalThis.localStorage.getItem(STORAGE_KEY);
  if (!raw) {
    return;
  }

  try {
    const saved = JSON.parse(raw);
    state.activeProjectId = saved.activeProjectId || null;
    state.activeRunId = saved.activeRunId || null;
    state.seenRunIds = new Set(Array.isArray(saved.seenRunIds) ? saved.seenRunIds : []);
    elements.taskPrompt.value = saved.taskPrompt || "";
    setSelectValue(elements.runMode, saved.runMode);
    setSelectValue(elements.permissionMode, saved.permissionMode);
    setSelectValue(elements.architectureReviewPreset, saved.architectureReviewPreset);
  } catch {
    globalThis.localStorage.removeItem(STORAGE_KEY);
  }
}

function clearLegacyAutofillPrompt() {
  if (LEGACY_AUTOFILL_PROMPTS.has(elements.taskPrompt.value.trim())) {
    elements.taskPrompt.value = "";
  }
}

function populateSelect(select, values) {
  select.replaceChildren();
  values.forEach(value => {
    const option = document.createElement("option");
    option.value = value;
    option.textContent = value;
    select.append(option);
  });
}

function setSelectValue(select, value) {
  if (!value) {
    return;
  }

  const option = Array.from(select.options).find(candidate => candidate.value === value);
  if (option) {
    select.value = value;
  }
}

function getSelectDisplayLabel(select) {
  const selectedOption = Array.from(select.options).find(option => option.value === select.value) || select.options[0] || null;
  return selectedOption?.textContent || selectedOption?.value || "Select";
}

function getComposerDropdownConfigs() {
  return [
    {
      id: "run-mode",
      select: elements.runMode,
      wrap: elements.runModeWrap,
      button: elements.runModeButton,
      label: elements.runModeLabel,
      menu: elements.runModeMenu
    },
    {
      id: "permission-mode",
      select: elements.permissionMode,
      wrap: elements.permissionModeWrap,
      button: elements.permissionModeButton,
      label: elements.permissionModeLabel,
      menu: elements.permissionModeMenu
    },
    {
      id: "architecture-review-preset",
      select: elements.architectureReviewPreset,
      wrap: elements.architectureReviewChip,
      button: elements.architectureReviewPresetButton,
      label: elements.architectureReviewPresetLabel,
      menu: elements.architectureReviewPresetMenu
    }
  ];
}

function renderComposerDropdowns() {
  getComposerDropdownConfigs().forEach(renderComposerDropdown);
}

function renderComposerDropdown(config) {
  const isOpen = state.composerMenuOpen === config.id;
  const options = Array.from(config.select.options);
  config.label.textContent = getSelectDisplayLabel(config.select);
  config.button.setAttribute("aria-expanded", isOpen ? "true" : "false");
  config.menu.replaceChildren();

  options.forEach(option => {
    const item = document.createElement("button");
    item.type = "button";
    item.className = "composer-dropdown-item";
    item.textContent = option.textContent || option.value;
    item.setAttribute("role", "menuitemradio");
    item.setAttribute("aria-checked", option.value === config.select.value ? "true" : "false");
    item.classList.toggle("current", option.value === config.select.value);
    item.addEventListener("click", event => {
      event.stopPropagation();
      selectComposerDropdownValue(config.id, option.value);
    });
    config.menu.append(item);
  });

  config.menu.classList.toggle("hidden", !isOpen || options.length === 0);
  config.wrap.classList.toggle("open", isOpen && options.length > 0);
  config.button.disabled = options.length === 0;
}

function closeComposerDropdowns() {
  if (!state.composerMenuOpen) {
    return;
  }

  state.composerMenuOpen = null;
  renderComposerDropdowns();
}

function toggleComposerDropdown(dropdownId) {
  if (state.composerMenuOpen === dropdownId) {
    state.composerMenuOpen = null;
    renderComposerDropdowns();
    return;
  }

  closeWorkspaceBranchMenu();
  state.composerMenuOpen = dropdownId;
  renderComposerDropdowns();
}

function selectComposerDropdownValue(dropdownId, value) {
  const config = getComposerDropdownConfigs().find(candidate => candidate.id === dropdownId);
  if (!config) {
    return;
  }

  if (config.select.value === value) {
    closeComposerDropdowns();
    return;
  }

  setSelectValue(config.select, value);
  closeComposerDropdowns();
  config.select.dispatchEvent(new Event("change", { bubbles: true }));
}

function createEmptyGitChangeReviewState() {
  return {
    projectId: null,
    currentBranch: null,
    targetBranch: null,
    files: [],
    selectedPath: null,
    diffByPath: {},
    loading: false,
    diffLoadingPath: null,
    error: "",
    stashInFlight: false,
    actionError: "",
    onCompleted: null,
    onClosed: null
  };
}

function isGitChangeReviewBranchSwitch(currentBranch, targetBranch) {
  if (!currentBranch || !targetBranch) {
    return false;
  }

  return !equalIgnoringCase(currentBranch, targetBranch);
}

function getGitChangeReviewSummary(currentBranch = "the current branch", targetBranch = "another branch") {
  if (!isGitChangeReviewBranchSwitch(currentBranch, targetBranch)) {
    return `Local changes were found on ${currentBranch}. Review them here before continuing.`;
  }

  const sourceLabel = currentBranch;
  const targetLabel = targetBranch;
  return `Switching from ${sourceLabel} to ${targetLabel} is blocked because there are local changes. Review them here, or stash them and continue the switch.`;
}

function getGitChangeStatusClass(status) {
  return String(status || "modified").toLowerCase().replaceAll(/[^a-z0-9]+/g, "-");
}

function createGitDiffMessageView(message) {
  const empty = document.createElement("div");
  empty.className = "git-diff-empty";
  empty.textContent = message;
  return empty;
}

function parseUnifiedDiff(diffText) {
  const lines = String(diffText || "").replaceAll("\r", "").split("\n");
  const sections = [];
  let currentFile = null;
  let currentHunk = null;

  const ensureFile = () => {
    if (!currentFile) {
      currentFile = {
        headerLines: [],
        hunks: []
      };
      sections.push(currentFile);
    }

    return currentFile;
  };

  lines.forEach(line => {
    if (line.startsWith("diff --git ")) {
      currentFile = {
        headerLines: [line],
        hunks: []
      };
      sections.push(currentFile);
      currentHunk = null;
      return;
    }

    if (line.startsWith("@@")) {
      const file = ensureFile();
      currentHunk = {
        header: line,
        lines: []
      };
      file.hunks.push(currentHunk);
      return;
    }

    if (currentHunk) {
      currentHunk.lines.push(line);
      return;
    }

    ensureFile().headerLines.push(line);
  });

  return sections;
}

function parseHunkHeader(header) {
  const match = /^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@/.exec(header || "");
  if (!match) {
    return { oldLine: 1, newLine: 1 };
  }

  return {
    oldLine: Number.parseInt(match[1], 10),
    newLine: Number.parseInt(match[3], 10)
  };
}

function createDiffRow(left, right, rowType) {
  return { left, right, rowType };
}

function createDiffSide(number, text, type) {
  return { number, text, type };
}

function createEmptyDiffSide() {
  return createDiffSide("", "", "empty");
}

function createMetaDiffRow(text) {
  return createDiffRow(
    createDiffSide("", text, "meta"),
    createDiffSide("", text, "meta"),
    "meta"
  );
}

function getPairRowType(deletions, additions) {
  if (deletions.length > 0 && additions.length > 0) {
    return "modify";
  }

  return deletions.length > 0 ? "delete" : "add";
}

function getDeletionType(additions) {
  return additions.length > 0 ? "modify-delete" : "delete";
}

function getAdditionType(deletions) {
  return deletions.length > 0 ? "modify-add" : "add";
}

function collectPrefixedLines(lines, startIndex, prefix) {
  const collected = [];
  let index = startIndex;
  while (index < lines.length && lines[index].startsWith(prefix)) {
    collected.push(lines[index]);
    index += 1;
  }

  return { collected, nextIndex: index };
}

function buildSideBySideRows(hunk) {
  const rows = [];
  const headerInfo = parseHunkHeader(hunk.header);
  let oldLine = headerInfo.oldLine;
  let newLine = headerInfo.newLine;
  let index = 0;

  const pushPairGroup = (deletions, additions) => {
    const rowType = getPairRowType(deletions, additions);
    const deletionType = getDeletionType(additions);
    const additionType = getAdditionType(deletions);
    const rowCount = Math.max(deletions.length, additions.length);
    for (let index = 0; index < rowCount; index += 1) {
      const deletion = deletions[index] || null;
      const addition = additions[index] || null;
      rows.push(createDiffRow(
        deletion
          ? createDiffSide(oldLine++, deletion.slice(1), deletionType)
          : createEmptyDiffSide(),
        addition
          ? createDiffSide(newLine++, addition.slice(1), additionType)
          : createEmptyDiffSide(),
        rowType
      ));
    }
  };

  while (index < hunk.lines.length) {
    const line = hunk.lines[index];
    if (line.startsWith("-")) {
      const deletionGroup = collectPrefixedLines(hunk.lines, index, "-");
      const additionGroup = collectPrefixedLines(hunk.lines, deletionGroup.nextIndex, "+");

      pushPairGroup(deletionGroup.collected, additionGroup.collected);
      index = additionGroup.nextIndex;
      continue;
    }

    if (line.startsWith("+")) {
      pushPairGroup([], [line]);
      index += 1;
      continue;
    }

    if (line.startsWith(" ")) {
      rows.push(createDiffRow(
        createDiffSide(oldLine++, line.slice(1), "context"),
        createDiffSide(newLine++, line.slice(1), "context"),
        "context"
      ));
      index += 1;
      continue;
    }

    rows.push(createMetaDiffRow(line));
    index += 1;
  }

  return rows;
}

function createDiffCell(side) {
  const cell = document.createElement("div");
  cell.className = `git-diff-cell git-diff-cell-${side.type}`;

  const lineNumber = document.createElement("span");
  lineNumber.className = "git-diff-line-number";
  lineNumber.textContent = side.number === "" ? "" : String(side.number);

  const content = document.createElement("span");
  content.className = "git-diff-line-content";
  content.textContent = side.text || "";

  cell.append(lineNumber, content);
  return cell;
}

function createSideBySideDiffView(diffText) {
  const sections = parseUnifiedDiff(diffText);
  if (!sections.length || sections.every(section => section.hunks.length === 0)) {
    return createGitDiffMessageView(diffText || "No textual diff is available for the selected file.");
  }

  const container = document.createElement("div");
  container.className = "git-diff-side-by-side";

  sections.forEach(section => {
    const sectionEl = document.createElement("section");
    sectionEl.className = "git-diff-section";

    const headerLines = section.headerLines.filter(Boolean);
    if (headerLines.length > 0) {
      const header = document.createElement("div");
      header.className = "git-diff-section-header";
      header.textContent = headerLines[headerLines.length - 1];
      sectionEl.append(header);
    }

    section.hunks.forEach(hunk => {
      const hunkEl = document.createElement("div");
      hunkEl.className = "git-diff-hunk";

      const hunkHeader = document.createElement("div");
      hunkHeader.className = "git-diff-hunk-header";
      hunkHeader.textContent = hunk.header;
      hunkEl.append(hunkHeader);

      const rows = document.createElement("div");
      rows.className = "git-diff-rows";

      buildSideBySideRows(hunk).forEach(row => {
        const rowEl = document.createElement("div");
        rowEl.className = `git-diff-row git-diff-row-${row.rowType}`;
        rowEl.append(createDiffCell(row.left), createDiffCell(row.right));
        rows.append(rowEl);
      });

      hunkEl.append(rows);
      sectionEl.append(hunkEl);
    });

    container.append(sectionEl);
  });

  return container;
}

function setGitDiffPreviewContent(view) {
  elements.gitDiffPreview.replaceChildren(view);
}

function toProjectBranchInfo(branchInfo) {
  if (!branchInfo) {
    return null;
  }

  return {
    isGitRepository: !!branchInfo.isGitRepository,
    currentBranch: branchInfo.currentBranch || null,
    branches: Array.isArray(branchInfo.branches) ? branchInfo.branches : []
  };
}

function applyProjectBranchInfo(projectId, branchInfo) {
  if (!projectId || !branchInfo) {
    return;
  }

  state.projectBranchInfoById[projectId] = toProjectBranchInfo(branchInfo);
}

function applyWorkingTreeStatusToGitChangeReview(workingTreeStatus) {
  if (!workingTreeStatus) {
    return;
  }

  state.gitChangeReview.currentBranch = workingTreeStatus.currentBranch || state.gitChangeReview.currentBranch;
  state.gitChangeReview.files = Array.isArray(workingTreeStatus.files) ? workingTreeStatus.files : [];

  const stillSelected = state.gitChangeReview.files.some(file => file.path === state.gitChangeReview.selectedPath);
  if (!stillSelected) {
    state.gitChangeReview.selectedPath = state.gitChangeReview.files[0]?.path || null;
  }

  state.gitChangeReview.diffByPath = Object.fromEntries(
    Object.entries(state.gitChangeReview.diffByPath).filter(([path]) => state.gitChangeReview.files.some(file => file.path === path))
  );
}

function renderGitChangeReview() {
  const review = state.gitChangeReview;
  const currentBranch = review.currentBranch || "Current branch";
  const requiresBranchSwitch = isGitChangeReviewBranchSwitch(review.currentBranch, review.targetBranch);
  let stashButtonLabel = "Stash changes";
  if (review.stashInFlight) {
    stashButtonLabel = "Stashing...";
  } else if (requiresBranchSwitch) {
    stashButtonLabel = `Stash and switch to ${review.targetBranch}`;
  }

  elements.gitChangesTitle.textContent = `Local changes on ${currentBranch}`;
  elements.gitChangesSummary.textContent = getGitChangeReviewSummary(review.currentBranch, review.targetBranch);
  elements.gitChangesActionStatus.textContent = review.actionError || (review.stashInFlight && requiresBranchSwitch ? "Creating stash and continuing the branch switch..." : "");
  elements.gitChangesStashButton.textContent = stashButtonLabel;
  elements.gitChangesCloseButton.textContent = requiresBranchSwitch ? "Close" : "Next";
  elements.gitChangesStashButton.classList.toggle("hidden", !requiresBranchSwitch);
  elements.gitChangesStashButton.disabled = review.loading
    || review.stashInFlight
    || !review.projectId
    || !requiresBranchSwitch
    || !Array.isArray(review.files)
    || review.files.length === 0;
  elements.gitChangeList.replaceChildren();

  if (review.loading && review.files.length === 0) {
    elements.gitChangeList.className = "git-change-list empty-state";
    elements.gitChangeList.textContent = "Loading changed files...";
    elements.gitDiffMeta.textContent = "Loading Git diff...";
    setGitDiffPreviewContent(createGitDiffMessageView("Loading changed files..."));
    return;
  }

  if (review.error) {
    elements.gitChangeList.className = "git-change-list empty-state";
    elements.gitChangeList.textContent = review.error;
    elements.gitDiffMeta.textContent = "Git diff unavailable";
    setGitDiffPreviewContent(createGitDiffMessageView(review.error));
    return;
  }

  if (!Array.isArray(review.files) || review.files.length === 0) {
    elements.gitChangeList.className = "git-change-list empty-state";
    elements.gitChangeList.textContent = "No local changes were found.";
    elements.gitDiffMeta.textContent = "No diff to show";
    setGitDiffPreviewContent(createGitDiffMessageView("No local changes were found."));
    return;
  }

  elements.gitChangeList.className = "git-change-list";

  review.files.forEach(file => {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "git-change-item";
    button.classList.toggle("active", file.path === review.selectedPath);

    const header = document.createElement("div");
    header.className = "git-change-item-header";

    const path = document.createElement("strong");
    path.className = "git-change-item-path";
    path.textContent = file.path;

    const statusBadge = document.createElement("span");
    statusBadge.className = `git-change-badge git-change-badge-status-${getGitChangeStatusClass(file.status)}`;
    statusBadge.textContent = file.status || "Modified";

    header.append(path, statusBadge);

    const meta = document.createElement("div");
    meta.className = "git-change-item-meta";
    if (file.isStaged) {
      const stagedBadge = document.createElement("span");
      stagedBadge.className = "git-change-badge";
      stagedBadge.textContent = "Staged";
      meta.append(stagedBadge);
    }
    if (file.isUntracked) {
      const untrackedBadge = document.createElement("span");
      untrackedBadge.className = "git-change-badge";
      untrackedBadge.textContent = "Untracked";
      meta.append(untrackedBadge);
    }
    if (file.previousPath) {
      const previousPathBadge = document.createElement("span");
      previousPathBadge.className = "git-change-badge";
      previousPathBadge.textContent = `from ${file.previousPath}`;
      meta.append(previousPathBadge);
    }

    button.append(header);
    if (meta.childElementCount > 0) {
      button.append(meta);
    }
    button.addEventListener("click", () => {
      if (review.selectedPath === file.path) {
        return;
      }

      state.gitChangeReview.selectedPath = file.path;
      renderGitChangeReview();
      void ensureSelectedGitDiff();
    });
    elements.gitChangeList.append(button);
  });

  const selectedFile = review.files.find(file => file.path === review.selectedPath) || review.files[0];
  if (!selectedFile) {
    elements.gitDiffMeta.textContent = "Select a changed file to view its diff.";
    setGitDiffPreviewContent(createGitDiffMessageView("Select a changed file to view its diff."));
    return;
  }

  state.gitChangeReview.selectedPath = selectedFile.path;
  const cachedDiff = review.diffByPath[selectedFile.path] || null;
  elements.gitDiffMeta.textContent = `${selectedFile.path} • ${selectedFile.status || "Modified"}`;
  if (review.diffLoadingPath === selectedFile.path) {
    setGitDiffPreviewContent(createGitDiffMessageView("Loading Git diff..."));
    return;
  }

  if (cachedDiff?.error) {
    setGitDiffPreviewContent(createGitDiffMessageView(cachedDiff.error));
    return;
  }

  if (cachedDiff?.diffText) {
    setGitDiffPreviewContent(createSideBySideDiffView(cachedDiff.diffText));
    return;
  }

  setGitDiffPreviewContent(createGitDiffMessageView("Select a changed file to view its diff."));
}

async function ensureSelectedGitDiff() {
  const review = state.gitChangeReview;
  if (!review.projectId || !review.selectedPath) {
    return;
  }

  if (review.diffByPath[review.selectedPath] || review.diffLoadingPath === review.selectedPath) {
    return;
  }

  state.gitChangeReview.diffLoadingPath = review.selectedPath;
  renderGitChangeReview();

  try {
    const response = await requestJson(`/api/projects/${encodeURIComponent(review.projectId)}/git/diff?path=${encodeURIComponent(review.selectedPath)}`);
    state.gitChangeReview.diffByPath[review.selectedPath] = {
      diffText: response?.diffText || "No textual diff is available for the selected file."
    };
  } catch (error) {
    state.gitChangeReview.diffByPath[review.selectedPath] = {
      error: error?.message || "Failed to load the selected Git diff."
    };
  } finally {
    state.gitChangeReview.diffLoadingPath = null;
    renderGitChangeReview();
  }
}

async function openGitChangeReview(projectId, targetBranch, branchInfo, options = {}) {
  state.gitChangeReview = createEmptyGitChangeReviewState();
  state.gitChangeReview.projectId = projectId;
  state.gitChangeReview.currentBranch = branchInfo?.currentBranch || null;
  state.gitChangeReview.targetBranch = isGitChangeReviewBranchSwitch(branchInfo?.currentBranch, targetBranch) ? targetBranch || null : null;
  state.gitChangeReview.onCompleted = typeof options.onCompleted === "function" ? options.onCompleted : null;
  state.gitChangeReview.onClosed = typeof options.onClosed === "function" ? options.onClosed : null;
  state.gitChangeReview.loading = true;
  renderGitChangeReview();
  openModal("git-changes-modal");

  try {
    const response = await requestJson(`/api/projects/${encodeURIComponent(projectId)}/git/changes`);
    state.gitChangeReview.currentBranch = response?.currentBranch || state.gitChangeReview.currentBranch;
    if (!isGitChangeReviewBranchSwitch(state.gitChangeReview.currentBranch, state.gitChangeReview.targetBranch)) {
      state.gitChangeReview.targetBranch = null;
    }
    state.gitChangeReview.files = Array.isArray(response?.files) ? response.files : [];
    state.gitChangeReview.selectedPath = state.gitChangeReview.files[0]?.path || null;
    state.gitChangeReview.loading = false;
    renderGitChangeReview();
    await ensureSelectedGitDiff();
  } catch (error) {
    state.gitChangeReview.loading = false;
    state.gitChangeReview.error = error?.message || "Failed to load local Git changes.";
    renderGitChangeReview();
  }
}

async function stashGitChangesAndContinue() {
  const review = state.gitChangeReview;
  if (!review.projectId || !review.targetBranch || review.stashInFlight) {
    return;
  }

  state.gitChangeReview.stashInFlight = true;
  state.gitChangeReview.actionError = "";
  renderGitChangeReview();

  try {
    const stashMessage = `ArchHarness stash before switching to ${review.targetBranch}`;
    const response = await requestJson(`/api/projects/${encodeURIComponent(review.projectId)}/git/stash`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ message: stashMessage })
    });

    applyProjectBranchInfo(review.projectId, response?.branchInfo);
    applyWorkingTreeStatusToGitChangeReview(response?.workingTreeStatus);

    const targetBranch = review.targetBranch;
    const projectId = review.projectId;
    const onCompleted = review.onCompleted;
    closeModal({ skipGitChangeReviewClose: true });
    await handleWorkspaceBranchSelection(projectId, targetBranch, { onSucceeded: onCompleted });
  } catch (error) {
    applyProjectBranchInfo(review.projectId, error?.data?.branchInfo);
    applyWorkingTreeStatusToGitChangeReview(error?.data?.workingTreeStatus);
    state.gitChangeReview.actionError = error?.message || "Failed to stash local changes.";
    renderGitChangeReview();
  } finally {
    if (state.openModalId === "git-changes-modal") {
      state.gitChangeReview.stashInFlight = false;
      renderGitChangeReview();
    }
  }
}

function getActiveProject() {
  return state.projects.find(project => project.projectId === state.activeProjectId) || null;
}

function syncComposerFromProject(project) {
  if (!project) {
    return;
  }

  setSelectValue(elements.permissionMode, project.permissionHandlerMode);
  setSelectValue(elements.runMode, project.architectureReviewMode ? "architecture-review" : "standard");
}

function getProjectRunCount(project) {
  return Array.isArray(project?.runs) ? project.runs.length : 0;
}

function timeAgo(value) {
  if (!value) return "";
  const date = value instanceof Date ? value : new Date(value);
  if (Number.isNaN(date.getTime())) return "";
  const secs = Math.floor((Date.now() - date.getTime()) / 1000);
  if (secs < 60) return `${secs}s ago`;
  const mins = Math.floor(secs / 60);
  if (mins < 60) return `${mins}m ago`;
  const hours = Math.floor(mins / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.floor(hours / 24);
  if (days < 30) return `${days}d ago`;
  const months = Math.floor(days / 30);
  if (months < 12) return `${months}mo ago`;
  return `${Math.floor(months / 12)}y ago`;
}

function runDateFromId(runId) {
  if (!runId || runId.length < 13) return null;
  const year = runId.slice(0, 4);
  const month = runId.slice(4, 6);
  const day = runId.slice(6, 8);
  const hour = runId.slice(9, 11);
  const minute = runId.slice(11, 13);
  return new Date(`${year}-${month}-${day}T${hour}:${minute}:00`);
}

function readEventField(entry, field) {
  if (!entry) {
    return null;
  }

  const pascalCase = field.charAt(0).toUpperCase() + field.slice(1);
  return entry[field] ?? entry[pascalCase] ?? null;
}

function formatTimestamp(value) {
  return value
    ? new Date(value).toLocaleString([], { hour: "2-digit", minute: "2-digit", month: "short", day: "numeric" })
    : "Pending";
}

function formatRunTimestamp(runId) {
  if (!runId || runId.length < 13) {
    return runId || "Unknown";
  }

  const year = runId.slice(0, 4);
  const month = runId.slice(4, 6);
  const day = runId.slice(6, 8);
  const hour = runId.slice(9, 11);
  const minute = runId.slice(11, 13);
  return `${year}-${month}-${day} ${hour}:${minute}`;
}

function summarizeWorkspacePath(path) {
  const normalized = String(path || "").replaceAll("\\", "/").replace(/\/$/, "");
  if (!normalized) {
    return "No workspace path";
  }

  const segments = normalized.split("/").filter(Boolean);
  return segments.length <= 3 ? normalized : `.../${segments.slice(-3).join("/")}`;
}

function escapeHtml(text) {
  return String(text ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}

// Security boundary: sanitizeHtml is the only approved path for injecting rendered HTML into the DOM.
// Every innerHTML or outerHTML write must go through sanitizeHtml or setSanitizedHtml so sink review stays centralized.
// Strips hostile tags and URI schemes to guard against XSS from locally-rendered server content.
function sanitizeHtml(html) {
  const parser = new DOMParser();
  const doc = parser.parseFromString(html || "", "text/html");
  doc.querySelectorAll("script,iframe,object,embed,form,base,meta,svg,math,use,link[rel=import]").forEach(el => el.remove());
  doc.querySelectorAll("*").forEach(el => {
    for (const attr of el.attributes) {
      const name = attr.name.toLowerCase();
      const trimmedValue = attr.value.trimStart().toLowerCase();
      const isUnsafeUri = trimmedValue.startsWith("javascript:")
        || trimmedValue.startsWith("data:")
        || trimmedValue.startsWith("vbscript:");
      const isUnsafeSrcSet = name === "srcset"
        && (trimmedValue.includes("data:") || trimmedValue.includes("javascript:") || trimmedValue.includes("vbscript:"));
      if (name.startsWith("on")
        || name === "style"
        || name === "formaction"
        || name === "xlink:href"
        || (name === "data" && (el.tagName === "OBJECT" || el.tagName === "EMBED"))
        || ((name === "href" || name === "src" || name === "action" || name === "poster" || name === "background") && isUnsafeUri)
        || isUnsafeSrcSet) {
        el.removeAttribute(attr.name);
      }
    }
  });
  return doc.body.innerHTML;
}

function setSanitizedHtml(element, html) {
  element.innerHTML = sanitizeHtml(html);
}

function closeEventStream(status = "idle") {
  if (state.eventSource) {
    state.eventSource.close();
    state.eventSource = null;
  }
  if (status === "idle" && state.streamOrder.length > 0) {
    showStreamCompleted();
  }
}

function getProjectById(projectId) {
  return state.projects.find(project => project.projectId === projectId) || null;
}

function getSelectedRun(project = getActiveProject()) {
  if (!project || !Array.isArray(project.runs)) {
    return null;
  }

  return project.runs.find(run => run.runId === state.activeRunId) || null;
}

function isSelectedRunLive() {
  return !!state.activeRun?.isRunning && state.activeRun?.runId === state.activeRunId;
}

function applyPersistedRunEvents(events) {
  resetStream();

  let submittedPrompt = null;
  (Array.isArray(events) ? events : []).forEach(entry => {
    const kind = readEventField(entry, "kind") || "";
    if (kind === "request") {
      submittedPrompt = readEventField(entry, "taskPrompt") || submittedPrompt;
      return;
    }

    if (kind === "agent-delta") {
      recordStreamEvent(entry);
    }
  });

  if (submittedPrompt) {
    syncSubmittedPromptSection(submittedPrompt);
  }

  if (state.streamOrder.length > 0) {
    showStreamCompleted();
  } else {
    elements.streamEmpty.textContent = DEFAULT_STREAM_EMPTY_MESSAGE;
    renderStream();
  }
}

async function loadSelectedRunStream() {
  const project = getActiveProject();
  const run = getSelectedRun(project);
  const token = ++state.selectedRunLoadToken;

  if (!project || !run) {
    state.selectedRunState = null;
    renderComposerState();
    closeEventStream(state.activeRun?.isRunning ? "reconnecting" : "idle");
    elements.streamEmpty.textContent = DEFAULT_STREAM_EMPTY_MESSAGE;
    resetStream();
    return;
  }

  await loadSelectedRunState(project, run);
  if (token !== state.selectedRunLoadToken) {
    return;
  }

  if (isSelectedRunLive()) {
    resetStream();
    syncSubmittedPromptSection(state.activeRun?.taskPrompt);
    connectEventStream();
    return;
  }

  closeEventStream("reconnecting");
  resetStream();
  showStreamStarting();

  try {
    const events = await requestJson(`/api/runs/${encodeURIComponent(run.runId)}/events?workspacePath=${encodeURIComponent(project.workspacePath)}`) || [];
    if (token !== state.selectedRunLoadToken) {
      return;
    }

    applyPersistedRunEvents(events);
  } catch (error) {
    if (token !== state.selectedRunLoadToken) {
      return;
    }

    resetStream();
    elements.streamEmpty.classList.remove("hidden");
    elements.streamEmpty.textContent = error?.message || "Failed to load persisted run events.";
  }
}

function isArchitectureModeEnabled() {
  return elements.runMode.value === "architecture-review";
}

function getPromptPlaceholder() {
  return isArchitectureModeEnabled()
    ? "Describe the architecture concern or boundary you want reviewed."
    : "Describe the change or review you want ArchHarness to run.";
}

function buildArchitecturePrompt(prompt) {
  if (!prompt) {
    return null;
  }

  if (elements.architectureReviewPreset.value === "full-review") {
    return `Run a full workspace architecture review. Focus area: ${prompt}`;
  }

  return prompt;
}

function buildPullRequestArchitecturePrompt(project) {
  const pr = reviewPrState.selectedPr;
  const title = pr?.Title || pr?.title || "Pull request";
  const pullRequestId = getReviewPrId(pr) || "unknown";
  const sourceBranch = getReviewPrSourceBranch(pr);
  const targetBranch = getReviewPrTargetBranch(pr);
  const repositoryName = pr?.RepositoryName || pr?.repositoryName || project?.displayName || "repository";
  const basePrompt = project?.architectureReviewPrompt
    || state.settings?.defaults?.architectureReviewPrompt
    || "Run an architecture review focused on the selected pull request changes.";
  const changedFiles = reviewPrState.prFiles
    .map(file => file.Path || file.path || file.FileName || file.fileName || "")
    .filter(Boolean)
    .slice(0, 200);

  const promptLines = [
    basePrompt,
    `Review pull request #${pullRequestId}: ${title}.`,
    `Repository: ${repositoryName}.`,
    sourceBranch ? `Source branch: ${sourceBranch}.` : "",
    targetBranch ? `Target branch: ${targetBranch}.` : "",
    changedFiles.length > 0 ? "Concentrate on these changed files first:" : ""
  ].filter(Boolean);

  if (changedFiles.length > 0) {
    changedFiles.forEach(path => {
      promptLines.push(`- ${path}`);
    });
  }

  promptLines.push("Identify architectural risks, boundary violations, coupling issues, missing abstractions, and regressions introduced by these changes.");
  return promptLines.join("\n");
}

function clearPendingInteractionPoll() {
  if (state.interactionPollHandle) {
    globalThis.clearTimeout(state.interactionPollHandle);
    state.interactionPollHandle = null;
  }
}

function abortPendingInteractionPoll() {
  if (state.pendingInteractionAbortController) {
    state.pendingInteractionAbortController.abort();
    state.pendingInteractionAbortController = null;
  }
}

function schedulePendingInteractionPoll(delayMs) {
  clearPendingInteractionPoll();

  if (state.isUnloading || document.hidden) {
    return;
  }

  state.interactionPollHandle = globalThis.setTimeout(() => {
    state.interactionPollHandle = null;
    void pollPendingInteraction();
  }, delayMs);
}

function openModal(modalId) {
  closeModal();
  const modal = document.getElementById(modalId);
  if (!modal) {
    return;
  }

  state.openModalId = modalId;
  modal.classList.remove("hidden");
  modal.setAttribute("aria-hidden", "false");
  elements.modalBackdrop.classList.remove("hidden");
}

function closeModal(options = {}) {
  const skipGitChangeReviewClose = options.skipGitChangeReviewClose === true;
  let gitChangeReviewClosedHandler = null;
  closeWorkspaceBranchMenu();
  closeComposerDropdowns();
  if (state.openModalId === "review-pr-modal") {
    abortPullRequestStream();
  }
  if (state.openModalId === "git-changes-modal") {
    gitChangeReviewClosedHandler = skipGitChangeReviewClose ? null : state.gitChangeReview.onClosed;
    state.gitChangeReview = createEmptyGitChangeReviewState();
  }

  if (!state.openModalId) {
    elements.modalBackdrop.classList.add("hidden");
    return;
  }

  const modal = document.getElementById(state.openModalId);
  if (modal) {
    modal.classList.add("hidden");
    modal.setAttribute("aria-hidden", "true");
  }

  state.openModalId = null;
  elements.modalBackdrop.classList.add("hidden");

  if (typeof gitChangeReviewClosedHandler === "function") {
    gitChangeReviewClosedHandler();
  }
}

function applyBootstrap(bootstrap) {
  state.bootstrap = bootstrap;
  populateSelect(elements.permissionMode, bootstrap.permissionModes || []);
  populateSelect(elements.newProjectPermission, bootstrap.permissionModes || []);
  populateSelect(elements.settingsPermissionMode, bootstrap.permissionModes || []);

  setSelectValue(elements.permissionMode, bootstrap.defaultPermissionHandlerMode);
  setSelectValue(elements.newProjectPermission, bootstrap.defaultPermissionHandlerMode);
  setSelectValue(elements.runMode, bootstrap.architectureLoopMode ? "architecture-review" : "standard");
  elements.newProjectPath.value = bootstrap.workspacePath || "";
  elements.newProjectArchitecture.checked = !!bootstrap.architectureLoopMode;
  elements.newProjectArchitecturePrompt.value = bootstrap.architectureLoopPrompt || "";
  elements.projectPickerNote.textContent = desktopBridge?.hostMode === "electron-local-web"
    ? "Desktop mode can open the system folder picker."
    : "Paste a workspace path here, or use the desktop picker.";
  state.activeRun = bootstrap.activeRun;
  renderComposerState();
  renderActiveRun();
}

async function pickProjectFolder() {
  const selectedPath = await selectFolderWithDesktopBridge({
    title: "Select Project Folder",
    unavailableMessage: "The system picker is only available in desktop mode.",
    unavailableTarget: elements.projectPickerNote
  });
  if (!selectedPath) {
    return;
  }

  elements.newProjectPath.value = selectedPath;
  if (!elements.newProjectName.value.trim()) {
    const segments = selectedPath.replaceAll("\\", "/").split("/").filter(Boolean);
    elements.newProjectName.value = segments[segments.length - 1] || selectedPath;
  }
}

async function selectFolderWithDesktopBridge({ title, unavailableMessage, unavailableTarget }) {
  if (!desktopBridge?.selectFolder) {
    if (unavailableTarget) {
      unavailableTarget.textContent = unavailableMessage;
    }

    return null;
  }

  return desktopBridge.selectFolder({ title });
}

async function pickReviewPrFolder() {
  const hintEl = document.getElementById("review-pr-folder-hint");
  const selectedPath = await selectFolderWithDesktopBridge({
    title: "Select PR Working Folder",
    unavailableMessage: "The system picker is only available in desktop mode. Enter the path manually here.",
    unavailableTarget: hintEl
  });
  if (!selectedPath) {
    return;
  }

  reviewPrState.folderPath = selectedPath;
  reviewPrState.projectId = null;
  reviewPrState.prFiles = [];
  const folderInput = document.getElementById("review-pr-folder-path");
  if (folderInput) {
    folderInput.value = selectedPath;
  }

  setReviewPrFolderHint();
  updateReviewPrNavigation();
}

function renderActiveRun() {
  const activeRun = state.activeRun;
  if (!activeRun) {
    elements.cancelRun.disabled = true;
    if (!state.isUnloading) {
      closeEventStream("idle");
    }
    renderTopbar();
    return;
  }
  elements.cancelRun.disabled = !activeRun.isRunning;

  if (activeRun.runId && !state.activeRunId) {
    state.activeRunId = activeRun.runId;
  }

  if (activeRun.isRunning && isSelectedRunLive() && !state.streamOrder.length) {
    syncSubmittedPromptSection(activeRun.taskPrompt);
  }

  if (!activeRun.isRunning && isSelectedRunLive() && !state.isUnloading) {
    closeEventStream("idle");
  }

  renderTopbar();
}

function renderTopbar() {
  const activeProject = getActiveProject();

  elements.workspaceTitle.textContent = activeProject ? activeProject.displayName : "No project selected";
  renderWorkspaceBranch(activeProject);
  void ensureActiveProjectBranchInfo();
  renderComposerState();
}

function renderWorkspaceBranch(activeProject) {
  if (!activeProject) {
    state.branchMenuOpen = false;
    elements.workspaceBranchWrap.classList.add("hidden");
    elements.workspaceBranchMenu.replaceChildren();
    elements.workspaceBranchLabel.textContent = "No branch";
    elements.workspaceBranchButton.disabled = true;
    elements.workspaceBranchButton.setAttribute("aria-expanded", "false");
    elements.workspaceBranchMenu.classList.add("hidden");
    elements.workspaceBranchWrap.classList.remove("open");
    return;
  }

  const branchInfo = state.projectBranchInfoById[activeProject.projectId] || null;
  let isDisabled = true;
  let buttonLabel;
  if (!branchInfo) {
    buttonLabel = "Loading branch...";
  } else if (!branchInfo.isGitRepository) {
    buttonLabel = "No Git repository";
  } else if (!Array.isArray(branchInfo.branches) || branchInfo.branches.length === 0) {
    buttonLabel = branchInfo.currentBranch || "Detached HEAD";
  } else {
    isDisabled = false;
    buttonLabel = branchInfo.currentBranch || branchInfo.branches[0] || "Detached HEAD";
  }

  elements.workspaceBranchLabel.textContent = state.branchSwitchProjectId === activeProject.projectId
    ? "Switching..."
    : buttonLabel;
  elements.workspaceBranchButton.disabled = isDisabled || state.branchSwitchProjectId === activeProject.projectId;
  elements.workspaceBranchButton.setAttribute("aria-expanded", state.branchMenuOpen ? "true" : "false");
  renderWorkspaceBranchMenu(activeProject, branchInfo);
  elements.workspaceBranchWrap.classList.remove("hidden");
}

function renderWorkspaceBranchMenu(activeProject, branchInfo) {
  elements.workspaceBranchMenu.replaceChildren();

  const branches = Array.isArray(branchInfo?.branches) ? branchInfo.branches : [];
  if (branches.length === 0) {
    elements.workspaceBranchMenu.classList.add("hidden");
    elements.workspaceBranchWrap.classList.remove("open");
    return;
  }

  branches.forEach(branch => {
    const item = document.createElement("button");
    item.type = "button";
    item.className = "branch-dropdown-item";
    item.textContent = branch;
    item.setAttribute("role", "menuitemradio");
    item.setAttribute("aria-checked", branch === branchInfo?.currentBranch ? "true" : "false");
    item.classList.toggle("current", branch === branchInfo?.currentBranch);
    item.disabled = state.branchSwitchProjectId === activeProject.projectId;
    item.addEventListener("click", () => {
      void handleWorkspaceBranchSelection(activeProject.projectId, branch).catch(error => {
        console.error("Branch switch failed:", error);
      });
    });
    elements.workspaceBranchMenu.append(item);
  });

  elements.workspaceBranchMenu.classList.toggle("hidden", !state.branchMenuOpen);
  elements.workspaceBranchWrap.classList.toggle("open", state.branchMenuOpen);
}

function closeWorkspaceBranchMenu() {
  if (!state.branchMenuOpen) {
    return;
  }

  state.branchMenuOpen = false;
  elements.workspaceBranchButton.setAttribute("aria-expanded", "false");
  elements.workspaceBranchMenu.classList.add("hidden");
  elements.workspaceBranchWrap.classList.remove("open");
}

function toggleWorkspaceBranchMenu() {
  if (elements.workspaceBranchButton.disabled) {
    return;
  }

  closeComposerDropdowns();
  state.branchMenuOpen = !state.branchMenuOpen;
  renderTopbar();
}

async function handleWorkspaceBranchSelection(projectId, branchName, options = {}) {
  const onSucceeded = typeof options.onSucceeded === "function" ? options.onSucceeded : null;
  const onBlocked = typeof options.onBlocked === "function" ? options.onBlocked : null;
  const onReviewClosed = typeof options.onReviewClosed === "function" ? options.onReviewClosed : null;
  const branchInfo = state.projectBranchInfoById[projectId] || null;
  if (branchName === branchInfo?.currentBranch) {
    return completeWorkspaceBranchSelection(onSucceeded);
  }

  state.branchSwitchProjectId = projectId;
  closeWorkspaceBranchMenu();
  renderTopbar();

  try {
    const response = await requestJson(`/api/projects/${encodeURIComponent(projectId)}/branch`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ branchName })
    });

    applyProjectBranchInfo(projectId, response);
    return completeWorkspaceBranchSelection(onSucceeded);
  } catch (error) {
    await handleWorkspaceBranchSelectionError(error, {
      projectId,
      branchName,
      branchInfo,
      onSucceeded,
      onBlocked,
      onReviewClosed
    });
    return false;
  } finally {
    state.branchSwitchProjectId = null;
    renderTopbar();
  }
}

async function completeWorkspaceBranchSelection(onSucceeded) {
  closeWorkspaceBranchMenu();
  await loadProjects();
  if (onSucceeded) {
    await onSucceeded();
  }

  return true;
}

function isBlockedWorkspaceBranchSwitch(error) {
  return error?.status === 409
    && (error?.data?.failureCode === "dirty-worktree" || error?.data?.failureCode === "checkout-conflict");
}

async function handleWorkspaceBranchSelectionError(error, context) {
  const latestBranchInfo = error?.data?.branchInfo ? toProjectBranchInfo(error.data.branchInfo) : context.branchInfo;

  if (latestBranchInfo) {
    state.projectBranchInfoById[context.projectId] = latestBranchInfo;
  }

  if (isBlockedWorkspaceBranchSwitch(error)) {
    context.onBlocked?.();
    await openGitChangeReview(context.projectId, context.branchName, latestBranchInfo, {
      onCompleted: context.onSucceeded,
      onClosed: context.onReviewClosed
    });
    return;
  }

  globalThis.alert(error?.message || "Failed to switch branches.");
}

async function ensureActiveProjectBranchInfo() {
  const activeProject = getActiveProject();
  if (!activeProject) {
    return;
  }

  const projectId = activeProject.projectId;
  if (state.projectBranchInfoById[projectId] || state.projectBranchRequestsInFlight.has(projectId)) {
    return;
  }

  state.projectBranchRequestsInFlight.add(projectId);

  try {
    const response = await requestJson(`/api/projects/${encodeURIComponent(projectId)}/branch`);
    state.projectBranchInfoById[projectId] = {
      isGitRepository: !!response?.isGitRepository,
      currentBranch: response?.currentBranch || null,
      branches: Array.isArray(response?.branches) ? response.branches : []
    };
  } catch {
    state.projectBranchInfoById[projectId] = {
      isGitRepository: false,
      currentBranch: null,
      branches: []
    };
  } finally {
    state.projectBranchRequestsInFlight.delete(projectId);
    renderTopbar();
  }
}

function renderComposerState() {
  const activeProject = getActiveProject();
  const architectureMode = isArchitectureModeEnabled();
  const selectedRun = getSelectedRun(activeProject);
  const showResumeButton = !!activeProject
    && !!selectedRun
    && !state.activeRun?.isRunning
    && !!state.selectedRunState?.canResume;
  elements.architectureReviewChip.classList.toggle("hidden", !architectureMode);
  elements.taskPrompt.placeholder = getPromptPlaceholder();
  elements.startRun.disabled = !activeProject || !elements.taskPrompt.value.trim();
  elements.resumeRun.classList.toggle("hidden", !showResumeButton);
  elements.resumeRun.disabled = !showResumeButton;
  elements.resumeRun.textContent = "Resume";
  renderComposerDropdowns();
}

function renderProjects() {
  elements.projectList.replaceChildren();
  if (state.projects.length === 0) {
    elements.projectList.className = "project-list empty-state";
    elements.projectList.textContent = "No projects yet.";
    renderTopbar();
    return;
  }

  elements.projectList.className = "project-list";
  state.projects.forEach(project => {
    const fragment = elements.projectTemplate.content.cloneNode(true);
    const card = fragment.querySelector(".project-card");
    const main = fragment.querySelector(".project-main");
    const title = fragment.querySelector(".project-title");
    const meta = fragment.querySelector(".project-meta");
    const runs = fragment.querySelector(".project-runs");

    const isActive = project.projectId === state.activeProjectId;
    const isExpanded = state.expandedProjectIds.has(project.projectId);
    title.textContent = project.displayName;
    meta.textContent = summarizeWorkspacePath(project.workspacePath);
    main.classList.toggle("active", isActive);
    card.classList.toggle("active", isActive);
    card.classList.toggle("expanded", isExpanded);

    main.addEventListener("click", () => {
      const wasActive = project.projectId === state.activeProjectId;
      if (!wasActive) {
        closeWorkspaceBranchMenu();
        closeComposerDropdowns();
      }
      state.activeProjectId = project.projectId;
      if (wasActive) {
        if (state.expandedProjectIds.has(project.projectId)) {
          state.expandedProjectIds.delete(project.projectId);
        } else {
          state.expandedProjectIds.add(project.projectId);
        }
      } else {
        state.expandedProjectIds.add(project.projectId);
      }
      syncComposerFromProject(project);
      saveShellState();
      renderProjects();
      renderTopbar();
    });

    if (!Array.isArray(project.runs) || project.runs.length === 0) {
      const empty = document.createElement("div");
      empty.className = "run-empty";
      empty.textContent = "No runs";
      runs.append(empty);
    } else {
      project.runs.forEach(run => {
        const runFragment = elements.runTemplate.content.cloneNode(true);
        const runLink = runFragment.querySelector(".run-link");
        const dotNode = runFragment.querySelector(".run-dot");
        const titleNode = runFragment.querySelector(".run-title");
        const timeNode = runFragment.querySelector(".run-time");
        const menuButton = runFragment.querySelector(".run-menu-button");

        titleNode.textContent = run.runTitle || `Run ${run.runId}`;
        const runDate = runDateFromId(run.runId);
        timeNode.textContent = timeAgo(runDate);
        runLink.classList.toggle("active", run.runId === state.activeRunId);

        const isLiveRun = state.activeRun?.isRunning && run.runId === state.activeRun?.runId;
        const isUnseen = !state.seenRunIds.has(run.runId);
        if (isLiveRun) {
          dotNode.classList.remove("hidden", "run-dot--done");
          dotNode.classList.add("run-dot--live");
        } else if (isUnseen) {
          dotNode.classList.remove("hidden", "run-dot--live");
          dotNode.classList.add("run-dot--done");
        } else {
          dotNode.classList.add("hidden");
        }

        runLink.addEventListener("click", () => {
          if (project.projectId !== state.activeProjectId) {
            closeWorkspaceBranchMenu();
            closeComposerDropdowns();
          }
          state.activeProjectId = project.projectId;
          state.activeRunId = run.runId;
          state.selectedRunState = null;
          if (!state.activeRun?.isRunning || state.activeRun?.runId !== run.runId) {
            state.seenRunIds.add(run.runId);
          }
          syncComposerFromProject(project);
          saveShellState();
          renderProjects();
          renderTopbar();
          void loadSelectedRunStream();
        });
        menuButton.addEventListener("click", event => {
          event.stopPropagation();
          void openRunDetails(project, run);
        });

        runs.append(runFragment);
      });
    }

    elements.projectList.append(fragment);
  });

  renderTopbar();
}

function ensureStreamSection(agentId, agentRole, title) {
  if (!state.streamSections[agentId]) {
    state.streamSections[agentId] = {
      agentId,
      agentRole,
      title: title || agentRole,
      segments: [],
      updatedAt: null,
      segmentCount: 0,
      streamKind: "assistant",
      renderHandle: null,
      renderVersion: 0
    };
    state.streamOrder.push(agentId);
  }

  const section = state.streamSections[agentId];
  section.agentRole = agentRole || section.agentRole;
  section.title = title || section.title || section.agentRole;
  return section;
}

function getOrCreateTextSegment(section) {
  const last = section.segments[section.segments.length - 1];
  if (last?.type === "text") return last;
  const seg = { type: "text", content: "", html: "" };
  section.segments.push(seg);
  return seg;
}

function getOrCreateToolGroup(section) {
  const last = section.segments[section.segments.length - 1];
  if (last?.type === "tool-group") return last;
  const seg = { type: "tool-group", calls: [] };
  section.segments.push(seg);
  return seg;
}

function formatToolCall(call) {
  try {
    const parsed = JSON.parse(call);
    if (parsed && typeof parsed.name === "string") {
      if (parsed.args && typeof parsed.args === "object") {
        const argStr = Object.entries(parsed.args)
          .map(([k, v]) => `${k}: ${JSON.stringify(v)}`)
          .join(", ");
        return argStr ? `${parsed.name}(${argStr})` : parsed.name;
      }
      return parsed.name;
    }
  } catch {
    // fall through to plain text
  }
  return call;
}

function renderToolGroupHtml(seg) {
  const count = seg.calls.length;
  const label = count === 1 ? "Tool call" : "Tool calls";
  const items = seg.calls.map(call =>
    `<li class="stream-tool-call-item">${escapeHtml(formatToolCall(call))}</li>`).join("");
  return `<details class="stream-tool-calls"><summary class="stream-tool-calls-header">${label} (${count})</summary><ul class="stream-tool-calls-list">${items}</ul></details>`;
}

function buildSectionBodyHtml(section) {
  if (section.segments.length === 0) {
    return `<pre>Waiting for rendered markdown...</pre>`;
  }
  return section.segments.map(seg => {
    if (seg.type === "tool-group") return renderToolGroupHtml(seg);
    return seg.html || (seg.content ? `<pre>${escapeHtml(seg.content)}</pre>` : "");
  }).join("");
}

function renderStream() {
  elements.streamSections.replaceChildren();
  const sections = state.streamOrder.map(agentId => state.streamSections[agentId]).filter(Boolean);
  const hasSections = sections.length > 0;
  elements.streamEmpty.classList.toggle("hidden", hasSections);
  if (hasSections) hideStreamStarting();

  sections.forEach(section => {
    const details = document.createElement("details");
    details.className = "stream-section";
    details.open = true;

    const summary = document.createElement("summary");
    summary.className = "stream-section-summary";

    const summaryLeft = document.createElement("div");
    const summaryTitle = document.createElement("strong");
    summaryTitle.textContent = section.title || section.agentRole;
    const summaryRole = document.createElement("span");
    summaryRole.textContent = section.agentRole || "agent";
    summaryLeft.append(summaryTitle, summaryRole);

    const summaryMeta = document.createElement("div");
    summaryMeta.className = "stream-section-meta";
    const summaryTime = document.createElement("span");
    summaryTime.textContent = formatTimestamp(section.updatedAt);
    summaryMeta.append(summaryTime);

    summary.append(summaryLeft, summaryMeta);

    const body = document.createElement("div");
    body.className = "markdown-surface stream-markdown";
    body.dataset.agentId = section.agentId;
    setSanitizedHtml(body, buildSectionBodyHtml(section));
    details.append(summary, body);
    elements.streamSections.append(details);
  });

  scrollStreamToBottom();
  renderTopbar();
}

function syncSubmittedPromptSection(promptText) {
  const submittedPrompt = String(promptText || "").trim();
  if (!submittedPrompt) {
    return;
  }

  const section = ensureStreamSection("submitted-run-prompt", "Run Request", "Submitted Prompt");
  section.segments = [
    {
      type: "text",
      content: submittedPrompt,
      html: ""
    }
  ];
  section.updatedAt = new Date().toISOString();
  section.segmentCount = 1;
  section.streamKind = "prompt";
  renderStream();
  scheduleStreamRender(section.agentId);
}

function scrollStreamToBottom() {
  if (!state.streamAutoScroll) return;
  const el = elements.streamSections;
  el.scrollTop = el.scrollHeight;
}

function showStreamStarting() {
  state.streamAutoScroll = true;
  const el = document.createElement("div");
  el.id = "stream-starting";
  el.className = "stream-starting";
  el.textContent = "Starting";
  elements.streamSections.append(el);
}

function hideStreamStarting() {
  const el = elements.streamSections.querySelector("#stream-starting");
  if (el) el.remove();
}

function agentDisplayName(source) {
  const names = {
    "backendDeveloper": "Backend Developer",
    "backend-developer": "Backend Developer",
    "BackendDeveloper": "Backend Developer",
    "frontendDeveloper": "Frontend Developer",
    "frontend-developer": "Frontend Developer",
    "FrontendDeveloper": "Frontend Developer",
    "codingStyle": "Coding Style",
    "coding-style": "Coding Style",
    "CodingStyle": "Coding Style",
    "security": "Security",
    "Security": "Security",
    "architecture": "Architecture",
    "Architecture": "Architecture",
    "orchestration": "Orchestration",
    "Orchestration": "Orchestration",
    "build": "Build",
    "Build": "Build"
  };
  return names[source] || source;
}

function showAgentSpinningUp(source) {
  const key = source.toLowerCase();
  if (state.agentSpinningUp[key]?.parentNode) {
    return;
  }
  const el = document.createElement("div");
  el.className = "stream-starting stream-agent-spinning-up";
  el.dataset.spinningKey = key;
  el.textContent = `Spinning up ${agentDisplayName(source)}`;
  state.agentSpinningUp[key] = el;
  elements.streamSections.append(el);
  scrollStreamToBottom();
}

function hideAgentSpinningUp(source) {
  const key = source.toLowerCase();
  const el = state.agentSpinningUp[key];
  if (el) {
    el.remove();
    delete state.agentSpinningUp[key];
  }
}

function showStreamCompleted() {
  const existing = elements.streamSections.querySelector("#stream-completed");
  if (existing) return;
  const el = document.createElement("div");
  el.id = "stream-completed";
  el.className = "stream-completed";
  el.textContent = "Completed";
  elements.streamSections.append(el);
  scrollStreamToBottom();
}

function scheduleStreamRender(agentId) {
  const section = state.streamSections[agentId];
  if (!section) {
    return;
  }

  if (section.renderHandle) {
    globalThis.clearTimeout(section.renderHandle);
  }

  section.renderHandle = globalThis.setTimeout(() => {
    section.renderHandle = null;
    void renderStreamSectionMarkdown(agentId);
  }, STREAM_RENDER_DELAY_MS);
}

async function renderStreamSectionMarkdown(agentId) {
  const section = state.streamSections[agentId];
  if (!section) {
    return;
  }

  const version = ++section.renderVersion;
  const textSegments = section.segments.filter(s => s.type === "text" && s.content);

  for (const seg of textSegments) {
    try {
      const response = await requestJson("/api/markdown/render", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ markdown: seg.content })
      });
      if (state.streamSections[agentId]?.renderVersion !== version) return;
      seg.html = response.html || `<pre>${escapeHtml(seg.content)}</pre>`;
    } catch {
      if (state.streamSections[agentId]?.renderVersion !== version) return;
      seg.html = `<pre>${escapeHtml(seg.content)}</pre>`;
    }
  }

  const container = elements.streamSections.querySelector(`[data-agent-id="${CSS.escape(agentId)}"]`);
  if (container) {
    setSanitizedHtml(container, buildSectionBodyHtml(section));
    scrollStreamToBottom();
  } else {
    renderStream();
  }
}

function recordStreamEvent(entry) {
  const agentId = readEventField(entry, "agentId");
  if (!agentId) {
    return;
  }

  const agentRole = readEventField(entry, "agentRole") || readEventField(entry, "source") || "unknown";
  const message = readEventField(entry, "message") || "";
  if (!message) {
    return;
  }

  const streamKind = readEventField(entry, "streamKind") || "assistant";
  const title = streamKind === "tool-call" ? null : readEventField(entry, "title");
  const section = ensureStreamSection(agentId, agentRole, title);
  if (section.segmentCount === 0) {
    hideAgentSpinningUp(agentRole);
  }

  if (streamKind === "tool-call") {
    const group = getOrCreateToolGroup(section);
    group.calls.push(message);
    section.segmentCount += 1;
    section.updatedAt = readEventField(entry, "timestampUtc") || new Date().toISOString();
    renderStream();
    return;
  }

  const seg = getOrCreateTextSegment(section);
  seg.content += message;
  section.segmentCount += 1;
  section.updatedAt = readEventField(entry, "timestampUtc") || new Date().toISOString();
  section.streamKind = streamKind;

  renderStream();
  scheduleStreamRender(agentId);
}

function resetStream() {
  Object.values(state.streamSections).forEach(section => {
    if (section.renderHandle) {
      globalThis.clearTimeout(section.renderHandle);
    }
  });
  state.streamSections = {};
  state.streamOrder = [];
  Object.keys(state.agentSpinningUp).forEach(key => {
    const el = state.agentSpinningUp[key];
    if (el?.parentNode) el.remove();
  });
  state.agentSpinningUp = {};
  renderStream();
}

async function loadBootstrap() {
  const bootstrap = await requestJson("/api/bootstrap");
  applyBootstrap(bootstrap);
}

async function warmModelDiscovery() {
  try {
    await requestJson("/api/preflight");
  } catch {
    // Model discovery is opportunistic for settings UX; the shell can still run on configured fallbacks.
  }
}

async function loadProjects() {
  state.projects = await requestJson("/api/projects?maxRunsPerProject=24") || [];
  const knownProjectIds = new Set(state.projects.map(project => project.projectId));
  state.projectBranchInfoById = Object.fromEntries(
    Object.entries(state.projectBranchInfoById).filter(([projectId]) => knownProjectIds.has(projectId))
  );
  const previousActiveProjectId = state.activeProjectId;
  if (!state.activeProjectId || !state.projects.some(project => project.projectId === state.activeProjectId)) {
    state.activeProjectId = state.projects[0]?.projectId || null;
  }
  if (previousActiveProjectId !== state.activeProjectId) {
    closeWorkspaceBranchMenu();
    closeComposerDropdowns();
  }
  if (state.activeProjectId) {
    state.expandedProjectIds.add(state.activeProjectId);
  }

  if (!state.activeRunId && state.projects.length > 0) {
    state.activeRunId = state.projects[0].runs?.[0]?.runId || null;
  }
  if (state.activeRunId) {
    const knownRunIds = new Set(state.projects.flatMap(project => Array.isArray(project.runs) ? project.runs.map(run => run.runId) : []));
    if (!knownRunIds.has(state.activeRunId)) {
      state.activeRunId = state.projects[0]?.runs?.[0]?.runId || null;
    }
  }

  syncComposerFromProject(getActiveProject());

  renderProjects();
  saveShellState();
}

async function loadSettings() {
  state.settings = await requestJson("/api/settings");
  const modelsResponse = await requestJson("/api/models");
  state.models = modelsResponse?.models || [];
  renderSettingsForm();
  applySettingsDefaults();
}

function applySettingsDefaults() {
  if (!state.settings) {
    return;
  }

  setSelectValue(elements.permissionMode, state.settings.defaults.permissionHandlerMode);
  setSelectValue(elements.settingsPermissionMode, state.settings.defaults.permissionHandlerMode);
  setSelectValue(elements.runMode, state.settings.defaults.architectureReviewMode ? "architecture-review" : "standard");
  elements.settingsArchitectureMode.checked = !!state.settings.defaults.architectureReviewMode;
  elements.settingsArchitecturePrompt.value = state.settings.defaults.architectureReviewPrompt || "";
}

function renderSettingsForm() {
  if (!state.settings) {
    return;
  }

  elements.settingsGrid.replaceChildren();
  Object.entries(ROLE_LABELS).forEach(([key, label]) => {
    const wrapper = document.createElement("label");
    wrapper.className = "field settings-field";
    const title = document.createElement("span");
    title.textContent = label;

    const select = document.createElement("select");
    select.id = `settings-model-${key}`;
    state.models.forEach(model => {
      const option = document.createElement("option");
      option.value = model.modelId;
      option.textContent = model.costBand
        ? `${model.displayName} • ${model.costBand}`
        : model.displayName;
      select.append(option);
    });

    setSelectValue(select, state.settings.agentModels[key]);
    wrapper.append(title, select);
    elements.settingsGrid.append(wrapper);
  });
}

function collectSettingsPayload() {
  const agentModels = {};
  Object.keys(ROLE_LABELS).forEach(key => {
    agentModels[key] = document.getElementById(`settings-model-${key}`).value;
  });

  return {
    agentModels,
    defaults: {
      permissionHandlerMode: elements.settingsPermissionMode.value,
      architectureReviewMode: elements.settingsArchitectureMode.checked,
      architectureReviewPrompt: elements.settingsArchitecturePrompt.value.trim() || null
    }
  };
}

function collectRunRequest() {
  const project = getActiveProject();
  if (!project) {
    throw new Error("Select a project before starting a run.");
  }

  const architectureLoopMode = isArchitectureModeEnabled();
  const prompt = elements.taskPrompt.value.trim();
  const architecturePrompt = architectureLoopMode
    ? buildArchitecturePrompt(prompt)
    : null;

  return {
    taskPrompt: architectureLoopMode ? "" : prompt,
    workspacePath: project.workspacePath,
    workspaceMode: project.workspaceMode,
    workflow: architectureLoopMode ? "architecture-loop" : "auto",
    projectName: project.displayName,
    projectId: project.projectId,
    modelOverrides: null,
    buildCommand: null,
    permissionHandlerMode: elements.permissionMode.value || project.permissionHandlerMode,
    reviewLoopAgents: state.bootstrap?.reviewLoopAgents || {
      codingStyleEnabled: true,
      securityEnabled: true,
      architectureEnabled: true
    },
    architectureLoopMode,
    architectureLoopPrompt: architectureLoopMode ? (architecturePrompt || project.architectureReviewPrompt || null) : null
  };
}

async function startRun() {
  const request = collectRunRequest();
  await submitRunRequest(request);
}

async function submitRunRequest(request) {
  resetStream();
  showStreamStarting();
  const snapshot = await requestJson("/api/runs", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request)
  });

  state.activeRun = snapshot;
  state.activeRunId = snapshot?.runId || state.activeRunId;
  elements.taskPrompt.value = "";
  saveShellState();
  renderActiveRun();
  connectEventStream();
  await loadProjects();
}

async function cancelRun() {
  state.activeRun = await requestJson("/api/runs/active", {
    method: "DELETE"
  });
  renderActiveRun();
  renderRunDetailsActions();
}

async function refreshActiveRun() {
  state.activeRun = await requestJson("/api/runs/active");
  if (state.activeRun?.runId && !state.activeRunId) {
    state.activeRunId = state.activeRun.runId;
  }
  renderActiveRun();
  renderRunDetailsActions();
  return state.activeRun;
}

function getSelectedProjectAndRun() {
  const project = state.projects.find(candidate => candidate.projectId === state.activeProjectId) || null;
  if (!project) {
    return { project: null, run: null };
  }

  const run = Array.isArray(project.runs)
    ? project.runs.find(candidate => candidate.runId === state.activeRunId) || null
    : null;
  return { project, run };
}

function renderRunDetailsActions() {
  renderComposerState();
}

async function loadSelectedRunState(project, run) {
  if (!project?.workspacePath || !run?.runId) {
    state.selectedRunState = null;
    renderComposerState();
    return;
  }

  try {
    state.selectedRunState = await requestJson(`/api/runs/${encodeURIComponent(run.runId)}/state?workspacePath=${encodeURIComponent(project.workspacePath)}`);
  } catch (error) {
    if (error?.status !== 404) {
      console.error("Load run state failed:", error);
    }

    state.selectedRunState = null;
  }

  renderComposerState();
}

async function resumeSelectedRun() {
  const { project, run } = getSelectedProjectAndRun();
  if (!project || !run) {
    return;
  }

  elements.resumeRun.disabled = true;
  elements.resumeRun.textContent = "Resuming...";

  try {
    state.activeRun = await requestJson(`/api/runs/${encodeURIComponent(run.runId)}/resume?workspacePath=${encodeURIComponent(project.workspacePath)}`, {
      method: "POST"
    });
    state.activeRunId = run.runId;
    saveShellState();
    renderActiveRun();
    connectEventStream();
    await loadProjects();
  } catch (error) {
    console.error("Resume failed:", error);
    renderComposerState();
  }
}

function connectEventStream() {
  if (state.eventSource || !state.activeRun?.isRunning || !isSelectedRunLive()) {
    return;
  }

  const eventSource = new EventSource("/api/runs/active/events");
  state.eventSource = eventSource;
  let sidebarRefreshed = false;

  const onEvent = async event => {
    const payload = JSON.parse(event.data);
    const kind = readEventField(payload, "kind") || "";
    if (kind === "agent-delta") {
      recordStreamEvent(payload);
    } else if (kind === "runtime-progress") {
      const message = readEventField(payload, "message") || "";
      const source = readEventField(payload, "source") || "";
      if (message.endsWith("prompt started") && source) {
        showAgentSpinningUp(source);
      }
    }

    const snapshot = await refreshActiveRun();
    if (!snapshot?.isRunning) {
      closeEventStream("idle");
      await loadProjects();
    } else if (!sidebarRefreshed && snapshot?.runId) {
      sidebarRefreshed = true;
      await loadProjects();
    }
  };

  eventSource.onmessage = onEvent;
  ["run-state", "runtime-progress", "agent-delta", "copilot-session"].forEach(kind => {
    eventSource.addEventListener(kind, onEvent);
  });

  eventSource.onerror = () => {
    if (state.isUnloading) {
      return;
    }

    closeEventStream(state.activeRun?.isRunning ? "reconnecting" : "idle");
    if (state.activeRun?.isRunning) {
      globalThis.setTimeout(connectEventStream, 1000);
    }
  };
}

function renderInlineInteraction() {
  const pending = state.pendingInteraction;
  if (!pending) {
    elements.inlineInteraction.classList.add("hidden");
    elements.inlineInteraction.replaceChildren();
    renderTopbar();
    return;
  }

  elements.inlineInteraction.classList.remove("hidden");
  elements.inlineInteraction.replaceChildren();

  const label = document.createElement("div");
  label.className = "inline-interaction-copy";
  const labelTitle = document.createElement("strong");
  labelTitle.textContent = pending.kind === "permission" ? "Permission" : "Input";
  const labelQuestion = document.createElement("p");
  labelQuestion.textContent = pending.question || "";
  label.append(labelTitle, labelQuestion);
  elements.inlineInteraction.append(label);

  if (pending.choices?.length) {
    const row = document.createElement("div");
    row.className = "choice-row";
    pending.choices.forEach(choice => {
      const button = document.createElement("button");
      button.type = "button";
      button.className = "choice-chip";
      button.textContent = choice;
      button.addEventListener("click", () => submitUserInput(choice));
      row.append(button);
    });
    elements.inlineInteraction.append(row);
  }

  if (pending.kind === "permission") {
    const actions = document.createElement("div");
    actions.className = "button-row";
    actions.append(
      interactionAction("Approve", "primary", () => submitPermission(true)),
      interactionAction("Deny", "danger", () => submitPermission(false))
    );
    elements.inlineInteraction.append(actions);
  } else {
    const input = document.createElement("textarea");
    input.rows = 3;
    input.placeholder = "Type your response";
    const actions = document.createElement("div");
    actions.className = "button-row";
    actions.append(interactionAction("Submit", "primary", () => submitUserInput(input.value)));
    elements.inlineInteraction.append(input, actions);
  }

  renderTopbar();
}

function interactionAction(label, tone, onClick) {
  const button = document.createElement("button");
  button.type = "button";
  button.className = `interaction-action ${tone}`;
  button.textContent = label;
  button.addEventListener("click", onClick);
  return button;
}

async function pollPendingInteraction() {
  if (state.pendingInteractionInFlight || state.isUnloading || document.hidden) {
    schedulePendingInteractionPoll(IDLE_INTERACTION_POLL_MS);
    return;
  }

  state.pendingInteractionInFlight = true;
  const controller = new AbortController();
  state.pendingInteractionAbortController = controller;

  try {
    state.pendingInteraction = await requestJson("/api/interactions/pending", { signal: controller.signal });
  } catch (error) {
    if (error?.name !== "AbortError") {
      state.pendingInteraction = null;
    }
  } finally {
    state.pendingInteractionAbortController = null;
    state.pendingInteractionInFlight = false;
    renderInlineInteraction();
    schedulePendingInteractionPoll(state.pendingInteraction ? ACTIVE_INTERACTION_POLL_MS : IDLE_INTERACTION_POLL_MS);
  }
}

async function submitUserInput(answer) {
  clearPendingInteractionPoll();
  abortPendingInteractionPoll();
  await requestJson("/api/interactions/user-input", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ answer })
  });
  state.pendingInteraction = null;
  renderInlineInteraction();
  await pollPendingInteraction();
}

async function submitPermission(approved) {
  clearPendingInteractionPoll();
  abortPendingInteractionPoll();
  await requestJson("/api/interactions/permission", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ approved })
  });
  state.pendingInteraction = null;
  renderInlineInteraction();
  await pollPendingInteraction();
}

async function createProject(event) {
  event.preventDefault();
  const payload = {
    displayName: elements.newProjectName.value.trim() || null,
    workspacePath: elements.newProjectPath.value.trim(),
    workspaceMode: "new-project",
    permissionHandlerMode: elements.newProjectPermission.value,
    architectureReviewMode: elements.newProjectArchitecture.checked,
    architectureReviewPrompt: elements.newProjectArchitecturePrompt.value.trim() || null
  };

  const project = await requestJson("/api/projects", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload)
  });

  closeWorkspaceBranchMenu();
  closeComposerDropdowns();
  state.activeProjectId = project.projectId;
  closeModal();
  elements.newProjectForm.reset();
  applyBootstrap(state.bootstrap || { workspaceModes: [], permissionModes: [] });
  await loadProjects();
}

async function saveSettings(event) {
  event.preventDefault();
  state.settings = await requestJson("/api/settings", {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(collectSettingsPayload())
  });
  applySettingsDefaults();
  closeModal();
}

// =================== Settings Tabs ===================

function switchSettingsTab(tabName) {
  document.querySelectorAll(".settings-tab").forEach(btn => {
    const isActive = btn.dataset.tab === tabName;
    btn.classList.toggle("active", isActive);
    btn.setAttribute("aria-selected", isActive ? "true" : "false");
  });
  document.getElementById("settings-tab-agent").classList.toggle("hidden", tabName !== "agent-settings");
  document.getElementById("settings-tab-providers").classList.toggle("hidden", tabName !== "source-control-providers");
}

// =================== Multi-Provider Setup ===================

const PROVIDER_META = {
  0: {
    numericValue: 0,
    label: "Azure DevOps Server",
    badgeClass: "provider-badge-ado-server",
    radioValue: "ado-server",
    visibleFields: ["serverUrl", "organization", "personalAccessToken"],
    fieldLabels: {
      organization: "Organization / Collection"
    },
    formValueFields: {
      serverUrl: "serverUrl",
      organization: "organization"
    },
    patHint: "Requires Code (Read) permission.",
    orgRequiredMessage: "Organization is required."
  },
  1: {
    numericValue: 1,
    label: "Azure DevOps Services",
    badgeClass: "provider-badge-ado-services",
    radioValue: "ado-services",
    visibleFields: ["organization", "personalAccessToken"],
    fieldLabels: {
      organization: "Organization"
    },
    formValueFields: {
      organization: "organization"
    },
    patHint: "Requires Code (Read) permission.",
    orgRequiredMessage: "Organization is required."
  },
  2: {
    numericValue: 2,
    label: "GitHub",
    badgeClass: "provider-badge-github",
    radioValue: "github",
    visibleFields: ["organization", "gitHubOwnerType", "personalAccessToken"],
    fieldLabels: {
      organization: "Owner / Organization"
    },
    formValueFields: {
      organization: "organization",
      gitHubOwnerType: "gitHubOwnerType"
    },
    patHint: "Optional for public repos; required for private repos.",
    orgRequiredMessage: "Owner or organization is required for GitHub."
  }
};

const RADIO_PROVIDER_MAP = Object.fromEntries(
  Object.values(PROVIDER_META).map(meta => [meta.radioValue, meta.numericValue])
);
const PROVIDER_ALLOWED_PROTOCOLS = new Set(["https:"]);
const PROVIDER_FORM_FIELDS = {
  serverUrl: {
    input: elements.providerServerUrl,
    wrapper: elements.providerServerUrlWrap
  },
  organization: {
    input: elements.providerOrg,
    wrapper: elements.providerOrgWrap,
    labelElement: elements.providerOrgLabel,
    defaultLabel: "Organization"
  },
  gitHubOwnerType: {
    input: elements.providerGitHubOwnerType,
    wrapper: elements.providerGitHubOwnerTypeWrap,
    defaultValue: "0"
  },
  personalAccessToken: {
    input: elements.providerPat,
    wrapper: elements.providerPatWrap
  }
};

function normalizeProviderField(value) {
  return String(value ?? "")
    .replaceAll(/[\u0000-\u001F\u007F]+/g, " ")
    .trim();
}

function normalizeProviderToken(value) {
  return String(value ?? "").trim();
}

function looksLikeProviderUrl(value) {
  if (!value) {
    return false;
  }

  try {
    const parsed = new URL(value);
    return parsed.protocol === "http:" || parsed.protocol === "https:";
  } catch {
    return false;
  }
}

function normalizeReviewLookupValue(value, maxLength = REVIEW_FILTER_MAX_LENGTH) {
  return String(value ?? "")
    .replaceAll(/[\u0000-\u001F\u007F]+/g, " ")
    .trim()
    .slice(0, maxLength);
}

function normalizeReviewPullRequestId(value) {
  const normalized = normalizeReviewLookupValue(value, REVIEW_PULL_REQUEST_ID_MAX_LENGTH);
  return /^\d+$/.test(normalized) ? normalized : "";
}

function normalizeProviderSummary(provider) {
  if (!provider || typeof provider !== "object") {
    return null;
  }

  const providerType = Number(provider.providerType ?? provider.provider);
  if (!Number.isInteger(providerType) || !Object.hasOwn(PROVIDER_META, providerType)) {
    return null;
  }

  return {
    providerType,
    displayName: normalizeProviderField(provider.displayName) || null,
    serverUrl: normalizeProviderField(provider.serverUrl) || null,
    organization: normalizeProviderField(provider.organization) || null,
    gitHubOwnerType: Number.isInteger(provider.gitHubOwnerType)
      ? Number(provider.gitHubOwnerType)
      : 0,
    personalAccessToken: null,
    personalAccessTokenStorageMode: Number.isInteger(provider.personalAccessTokenStorageMode)
      ? Number(provider.personalAccessTokenStorageMode)
      : PAT_STORAGE_MODE_PROTECTED,
    isEnabled: provider.isEnabled !== false
  };
}

function normalizeProviderCollection(result) {
  let providers = [];
  if (Array.isArray(result)) {
    providers = result;
  } else if (Array.isArray(result?.providers)) {
    providers = result.providers;
  }

  return providers
    .map(normalizeProviderSummary)
    .filter(provider => provider !== null);
}

function getSelectedProviderRadioValue() {
  return document.querySelector('input[name="pf-type"]:checked')?.value || null;
}

function getProviderMetaByType(providerType) {
  if (providerType == null) {
    return null;
  }

  return PROVIDER_META[providerType] || null;
}

function getProviderMetaByRadioValue(radioValue) {
  return getProviderMetaByType(radioValue == null ? null : RADIO_PROVIDER_MAP[radioValue]);
}

function setProviderStatus(message = "", tone = null) {
  elements.providerTestStatus.textContent = message;
  elements.providerTestStatus.className = tone
    ? `sc-status sc-status-${tone}`
    : "sc-status";
}

function setProviderPatMasked(masked) {
  elements.providerPat.type = masked ? "password" : "text";
  elements.providerPatToggleIcon.className = masked ? "fa-solid fa-eye" : "fa-solid fa-eye-slash";
  elements.providerPatToggle.setAttribute("aria-label", masked ? "Show token" : "Hide token");
  elements.providerPatToggle.setAttribute("aria-pressed", masked ? "false" : "true");
}

function buildProviderPatHint(providerMeta) {
  if (!providerMeta) {
    return "";
  }

  const baseHint = providerMeta.patHint || "Requires Code (Read) permission.";

  if (!state.editingProviderName) {
    return baseHint;
  }

  if (providerMeta.numericValue === 2) {
    return `${baseHint} Leave blank to save without a token.`;
  }

  return `${baseHint} Leave blank to keep the current token.`;
}

function validateProviderServerUrl(serverUrl) {
  let parsedUrl;
  try {
    parsedUrl = new URL(serverUrl);
  } catch {
    return "Server URL must be an absolute HTTPS URL.";
  }

  if (!PROVIDER_ALLOWED_PROTOCOLS.has(parsedUrl.protocol)) {
    return "Server URL must use the https scheme.";
  }

  if (parsedUrl.username || parsedUrl.password) {
    return "Server URL cannot include embedded credentials.";
  }

  return null;
}

async function loadProviders() {
  try {
    const result = await requestJson("/api/providers");
    state.providers = normalizeProviderCollection(result);
  } catch {
    state.providers = [];
  }
  renderProviderList();
}

function renderProviderList() {
  elements.providerList.replaceChildren();

  if (!state.providers || state.providers.length === 0) {
    const empty = document.createElement("p");
    empty.className = "provider-list-empty";
    empty.textContent = "No providers configured.";
    elements.providerList.append(empty);
    return;
  }

  state.providers.forEach(provider => {
    const item = document.createElement("div");
    item.className = "provider-item";

    const info = document.createElement("div");
    info.className = "provider-item-info";

    const name = document.createElement("strong");
    name.className = "provider-item-name";
    name.textContent = provider.displayName || "Unnamed";

    const badge = document.createElement("span");
    const providerMeta = getProviderMetaByType(provider.providerType);
    badge.className = `provider-badge ${providerMeta?.badgeClass || ""}`;
    badge.textContent = providerMeta?.label || "Unknown";

    const storageBadge = document.createElement("span");
    storageBadge.className = `provider-badge ${provider.personalAccessTokenStorageMode === PAT_STORAGE_MODE_PLAINTEXT
      ? "provider-badge-plaintext"
      : "provider-badge-protected"}`;
    storageBadge.textContent = provider.personalAccessTokenStorageMode === PAT_STORAGE_MODE_PLAINTEXT
      ? "Plain Text PAT"
      : "Protected PAT";

    info.append(name, badge, storageBadge);

    const actions = document.createElement("div");
    actions.className = "provider-item-actions";

    const editBtn = document.createElement("button");
    editBtn.className = "ghost-button small-button";
    editBtn.type = "button";
    editBtn.textContent = "Edit";
    editBtn.addEventListener("click", () => openProviderSetup(provider));

    const deleteBtn = document.createElement("button");
    deleteBtn.className = "ghost-button small-button danger-button";
    deleteBtn.type = "button";
    deleteBtn.textContent = "Delete";
    deleteBtn.addEventListener("click", () => {
      void confirmDeleteProvider(provider.displayName).catch(err => console.error("Delete provider failed:", err));
    });

    actions.append(editBtn, deleteBtn);
    item.append(info, actions);
    elements.providerList.append(item);
  });
}

function openProviderSetup(provider = null) {
  const normalizedProvider = normalizeProviderSummary(provider);
  const providerMeta = getProviderMetaByType(normalizedProvider?.providerType ?? null);
  state.editingProviderName = normalizedProvider?.displayName || null;
  state.editingProviderStorageMode = normalizedProvider?.personalAccessTokenStorageMode ?? PAT_STORAGE_MODE_PROTECTED;
  state.providerConnectionTested = false;

  elements.providerTypeRadios.forEach(radio => { radio.checked = false; });
  elements.providerDisplayName.value = "";
  Object.values(PROVIDER_FORM_FIELDS).forEach(field => {
    field.input.value = field.defaultValue || "";
    field.wrapper.classList.add("hidden");
    if (field.labelElement) {
      field.labelElement.textContent = field.defaultLabel || "";
    }
  });
  setProviderPatMasked(true);
  setProviderStatus();

  if (normalizedProvider) {
    const radioValue = providerMeta?.radioValue;
    const radio = radioValue
      ? document.querySelector(`input[name="pf-type"][value="${radioValue}"]`)
      : null;
    if (radio) {
      radio.checked = true;
      onProviderSetupTypeChange();
    }

    elements.providerDisplayName.value = normalizedProvider.displayName || "";

    Object.entries(providerMeta?.formValueFields || {}).forEach(([fieldKey, providerKey]) => {
      const field = PROVIDER_FORM_FIELDS[fieldKey];
      if (field) {
        field.input.value = normalizedProvider[providerKey] || "";
      }
    });
  }

  elements.providerList.classList.add("hidden");
  elements.btnAddProvider.classList.add("hidden");
  elements.providerSetup.classList.remove("hidden");
  elements.btnSaveProvider.textContent = provider ? "Update Provider" : "Save Provider";
  onProviderSetupTypeChange();
}

function closeProviderSetup() {
  elements.providerSetup.classList.add("hidden");
  elements.providerList.classList.remove("hidden");
  elements.btnAddProvider.classList.remove("hidden");
  state.editingProviderName = null;
  state.editingProviderStorageMode = PAT_STORAGE_MODE_PROTECTED;
  state.providerConnectionTested = false;
  setProviderPatMasked(true);
  setProviderStatus();
}

function onProviderSetupTypeChange() {
  const meta = getProviderMetaByRadioValue(getSelectedProviderRadioValue());

  Object.entries(PROVIDER_FORM_FIELDS).forEach(([fieldKey, field]) => {
    const isVisible = meta?.visibleFields.includes(fieldKey) || false;
    field.wrapper.classList.toggle("hidden", !isVisible);
    if (field.labelElement) {
      field.labelElement.textContent = meta?.fieldLabels?.[fieldKey] || field.defaultLabel || "";
    }
  });

  elements.providerPatHint.textContent = buildProviderPatHint(meta);
  setProviderStatus();
}

function getProviderFieldValues(providerMeta) {
  return Object.fromEntries(
    Object.entries(providerMeta.formValueFields).map(([fieldKey, providerKey]) => [
      providerKey,
      normalizeProviderField(PROVIDER_FORM_FIELDS[fieldKey].input.value)
    ])
  );
}

function syncProviderFieldValues(providerMeta, normalizedFieldValues) {
  Object.entries(providerMeta.formValueFields).forEach(([fieldKey, providerKey]) => {
    PROVIDER_FORM_FIELDS[fieldKey].input.value = normalizedFieldValues[providerKey]
      || PROVIDER_FORM_FIELDS[fieldKey].defaultValue
      || "";
  });
}

function validateGitHubOwnerType(providerMeta, gitHubOwnerType) {
  if (providerMeta.numericValue !== 2) {
    return null;
  }

  return Number.isInteger(gitHubOwnerType)
    ? null
    : "Select whether the GitHub owner is an organization or a user.";
}

function validateProviderPayloadInputs(providerMeta, values) {
  if (!values.displayName) {
    return "Display name is required.";
  }

  if (/[\\/]/.test(values.displayName)) {
    return "Display name cannot contain path separator characters.";
  }

  if (!values.organization) {
    return providerMeta.orgRequiredMessage;
  }

  if (providerMeta.visibleFields.includes("serverUrl")) {
    if (!values.serverUrl) {
      return "Server URL is required for Azure DevOps Server.";
    }

    const serverUrlError = validateProviderServerUrl(values.serverUrl);
    if (serverUrlError) {
      return serverUrlError;
    }
  }

  if (values.requirePersonalAccessToken && !values.personalAccessToken) {
    return "Enter a personal access token to test the connection.";
  }

  if (values.personalAccessToken && looksLikeProviderUrl(values.personalAccessToken)) {
    return "Personal access token looks like a URL. Check browser autofill and re-enter the token.";
  }

  return validateGitHubOwnerType(providerMeta, values.gitHubOwnerType);
}

function collectProviderPayload(options = {}) {
  const personalAccessTokenStorageMode = Number.isInteger(options.personalAccessTokenStorageMode)
    ? options.personalAccessTokenStorageMode
    : state.editingProviderStorageMode;
  const providerMeta = getProviderMetaByRadioValue(getSelectedProviderRadioValue());
  if (!providerMeta) {
    return { payload: null, error: "Select a source control provider." };
  }

  const requirePersonalAccessToken = options.requirePersonalAccessToken === true
    && providerMeta.numericValue !== 2;

  const displayName = normalizeProviderField(elements.providerDisplayName.value);
  const personalAccessToken = normalizeProviderToken(elements.providerPat.value);
  const normalizedFieldValues = getProviderFieldValues(providerMeta);
  const organization = normalizedFieldValues.organization || "";
  const serverUrl = normalizedFieldValues.serverUrl || null;
  const gitHubOwnerType = Number.parseInt(normalizedFieldValues.gitHubOwnerType || "0", 10);

  elements.providerDisplayName.value = displayName;
  elements.providerPat.value = personalAccessToken;
  syncProviderFieldValues(providerMeta, normalizedFieldValues);

  const validationError = validateProviderPayloadInputs(providerMeta, {
    displayName,
    organization,
    serverUrl,
    personalAccessToken,
    requirePersonalAccessToken,
    gitHubOwnerType
  });
  if (validationError) {
    return { payload: null, error: validationError };
  }

  const payload = {
    provider: providerMeta.numericValue,
    displayName,
    personalAccessToken: personalAccessToken || null,
    personalAccessTokenStorageMode,
    isEnabled: true,
    serverUrl,
    organizationUrl: null,
    organization,
    gitHubOwnerType: providerMeta.numericValue === 2 ? gitHubOwnerType : 0
  };

  return { payload, error: null };
}

async function testProviderConnection() {
  const { payload: config, error } = collectProviderPayload({ requirePersonalAccessToken: true });
  const btn = elements.btnTestProvider;

  if (!config) {
    setProviderStatus(error || "Select a provider, enter a display name, and fill in the required fields.", "error");
    return;
  }

  btn.disabled = true;
  setProviderStatus("Testing...", null);
  state.providerConnectionTested = false;

  try {
    const result = await requestJson("/api/providers/test", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(config)
    });
    if (result.success) {
      setProviderStatus(result.message || "Connection successful.", "success");
      state.providerConnectionTested = true;
    } else {
      setProviderStatus(`Connection failed: ${result.message}`, "error");
    }
  } catch (error) {
    setProviderStatus(`Connection failed: ${error.message}`, "error");
  } finally {
    btn.disabled = false;
  }
}

function shouldOfferPlainTextProviderFallback(error, payload) {
  return error?.status === 409
    && error?.data?.code === "pat-protection-unavailable"
    && payload.personalAccessTokenStorageMode !== PAT_STORAGE_MODE_PLAINTEXT;
}

function confirmPlainTextProviderFallback(warning) {
  return globalThis.confirm(`${warning}\n\nSelect OK to store the token in plain text for this provider, or Cancel to keep editing.`);
}

async function persistProviderWithFallback(payload) {
  let savePayload = { ...payload };

  while (true) {
    try {
      await requestJson("/api/providers", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(savePayload)
      });
      return savePayload;
    } catch (requestError) {
      if (!shouldOfferPlainTextProviderFallback(requestError, savePayload)) {
        throw requestError;
      }

      const warning = requestError.data.warning || "Secure token storage is unavailable on this platform.";
      const confirmed = confirmPlainTextProviderFallback(warning);
      if (!confirmed) {
        setProviderStatus("Provider was not saved. Secure token storage is unavailable on this platform.", "error");
        return null;
      }

      savePayload = {
        ...savePayload,
        personalAccessTokenStorageMode: PAT_STORAGE_MODE_PLAINTEXT
      };
      state.editingProviderStorageMode = PAT_STORAGE_MODE_PLAINTEXT;
    }
  }
}

async function saveProvider() {
  const { payload, error } = collectProviderPayload({ requirePersonalAccessToken: false });

  if (!payload) {
    setProviderStatus(error || "Select a provider, enter a display name, and fill in the required fields.", "error");
    return;
  }

  if (state.editingProviderName
    && state.editingProviderName !== payload.displayName
    && payload.provider !== 2
    && !payload.personalAccessToken) {
    setProviderStatus("Enter a personal access token when renaming a provider so the saved credential can be preserved.", "error");
    return;
  }

  elements.btnSaveProvider.disabled = true;

  try {
    const savePayload = await persistProviderWithFallback(payload);
    if (!savePayload) {
      return;
    }

    if (state.editingProviderName && state.editingProviderName !== savePayload.displayName) {
      await requestJson(`/api/providers/${encodeURIComponent(state.editingProviderName)}`, { method: "DELETE" });
    }

    await loadProviders();
    renderProviderList();
    closeProviderSetup();
  } catch (error) {
    setProviderStatus(`Save failed: ${error.message}`, "error");
  } finally {
    elements.btnSaveProvider.disabled = false;
  }
}

async function confirmDeleteProvider(displayName) {
  if (!globalThis.confirm(`Delete provider "${displayName}"?`)) return;

  try {
    await requestJson(`/api/providers/${encodeURIComponent(displayName)}`, { method: "DELETE" });
    state.providers = state.providers.filter(p => p.displayName !== displayName);
    renderProviderList();
  } catch (error) {
    console.error("Delete provider failed:", error);
  }
}

async function openRunDetails(project, run) {
  if (project.projectId !== state.activeProjectId) {
    closeWorkspaceBranchMenu();
    closeComposerDropdowns();
  }
  state.activeProjectId = project.projectId;
  state.activeRunId = run.runId;
  saveShellState();
  void loadSelectedRunStream();
  elements.runDetailsTitle.textContent = run.runTitle || `Run ${run.runId}`;
  elements.artifactSummary.textContent = `${formatRunTimestamp(run.runId)} • ${project.displayName}`;
  elements.artifactPreview.textContent = "Loading artifacts...";
  openModal("run-details-modal");

  state.artifacts = await requestJson(`/api/runs/${encodeURIComponent(run.runId)}/artifacts?workspacePath=${encodeURIComponent(project.workspacePath)}`) || [];
  state.selectedArtifactPath = state.artifacts[0]?.fullPath || null;
  renderArtifacts();
}

function renderArtifacts() {
  elements.artifactList.replaceChildren();
  if (state.artifacts.length === 0) {
    elements.artifactList.className = "artifact-list empty-state";
    elements.artifactList.textContent = "No artifacts found for this run.";
    elements.artifactPreview.textContent = "Artifact previews appear here.";
    return;
  }

  elements.artifactList.className = "artifact-list";
  state.artifacts.forEach(artifact => {
    const fragment = elements.artifactTemplate.content.cloneNode(true);
    const button = fragment.querySelector(".artifact-item");
    fragment.querySelector(".artifact-item-title").textContent = artifact.name;
    fragment.querySelector(".artifact-item-kind").textContent = artifact.kind;
    fragment.querySelector(".artifact-item-description").textContent = artifact.description;
    button.classList.toggle("active", artifact.fullPath === state.selectedArtifactPath);
    button.addEventListener("click", () => {
      state.selectedArtifactPath = artifact.fullPath;
      renderArtifacts();
    });
    elements.artifactList.append(button);
  });

  const selected = state.artifacts.find(artifact => artifact.fullPath === state.selectedArtifactPath) || state.artifacts[0];
  state.selectedArtifactPath = selected.fullPath;
  elements.artifactSummary.textContent = `${selected.name} • ${selected.kind}`;
  elements.artifactPreview.textContent = selected.preview || "Artifact previews appear here.";
}

// =================== Review PR ===================

const REVIEW_PR_STEP_TITLES = ["Select Provider", "Select Pull Request", "Working Folder", "Confirm Review"];
const REVIEW_PR_STEP_IDS = ["review-pr-step-provider", "review-pr-step-list", "review-pr-step-folder", "review-pr-step-confirm"];

async function openReviewPrModal() {
  abortPullRequestStream();
  reviewPrState = {
    step: 0,
    providers: [],
    selectedProvider: null,
    autoSelectedProvider: false,
    allPullRequests: [],
    pullRequests: [],
    selectedProjects: [],
    selectedRepositories: [],
    selectedAuthors: [],
    pullRequestStreamController: null,
    isPullRequestStreamLoading: false,
    isPullRequestStreamComplete: false,
    pullRequestError: "",
    selectedPr: null,
    projectId: null,
    folderPath: "",
    prFiles: [],
    prFilesError: "",
    isPreparingWorkspace: false,
    isStartingReview: false
  };

  let providers;
  try {
    providers = await requestJson("/api/providers");
  } catch {
    alert("Failed to load providers. Check your connection and try again.");
    return;
  }

  const enabled = normalizeProviderCollection(providers).filter(p => p.isEnabled);

  if (enabled.length === 0) {
    alert("No source control providers are enabled. Configure a provider in Settings first.");
    return;
  }

  reviewPrState.providers = enabled;

  if (enabled.length === 1) {
    reviewPrState.selectedProvider = enabled[0];
    reviewPrState.autoSelectedProvider = true;
    showReviewPrStep(1);
  } else {
    renderProviderPicker();
    showReviewPrStep(0);
  }

  openModal("review-pr-modal");

  if (reviewPrState.autoSelectedProvider) {
    await loadPullRequests();
  }
}

function showReviewPrStep(i) {
  reviewPrState.step = i;

  REVIEW_PR_STEP_IDS.forEach(id => {
    const el = document.getElementById(id);
    if (el) el.classList.add("hidden");
  });

  const stepEl = document.getElementById(REVIEW_PR_STEP_IDS[i]);
  if (stepEl) stepEl.classList.remove("hidden");

  const titleEl = document.getElementById("review-pr-modal-title");
  if (titleEl) titleEl.textContent = REVIEW_PR_STEP_TITLES[i] || "Review PR";

  const backBtn = document.getElementById("review-pr-back-button");

  const showBack = i > 0 && !(i === 1 && reviewPrState.autoSelectedProvider);
  backBtn.classList.toggle("hidden", !showBack);

  updateReviewPrNavigation();
}

function updateReviewPrNavigation() {
  const nextBtn = document.getElementById("review-pr-next-button");
  const goBtn = elements.reviewPrGoButton;
  if (!nextBtn) {
    return;
  }

  if (goBtn) {
    const showGoButton = reviewPrState.step === 3;
    goBtn.classList.toggle("hidden", !showGoButton);
    goBtn.disabled = !showGoButton || reviewPrState.isStartingReview || !reviewPrState.projectId;
    goBtn.textContent = reviewPrState.isStartingReview ? "..." : "GO";
  }

  nextBtn.classList.toggle("hidden", reviewPrState.step === 3);

  if (reviewPrState.step === 3) {
    nextBtn.textContent = reviewPrState.isStartingReview ? "Starting review..." : "Start Review";
  } else if (reviewPrState.step === 2) {
    nextBtn.textContent = reviewPrState.isPreparingWorkspace ? "Preparing..." : "Next";
  } else {
    nextBtn.textContent = "Next";
  }

  if (reviewPrState.step === 0) {
    nextBtn.disabled = !reviewPrState.selectedProvider;
  } else if (reviewPrState.step === 1) {
    nextBtn.disabled = !reviewPrState.selectedPr;
  } else if (reviewPrState.step === 2) {
    nextBtn.disabled = reviewPrState.isPreparingWorkspace || !reviewPrState.folderPath.trim();
  } else {
    nextBtn.disabled = reviewPrState.isStartingReview || !reviewPrState.projectId;
  }
}

function getReviewPrFolderBaseHint() {
  return desktopBridge?.hostMode === "electron-local-web"
    ? "Use Browse to choose the local folder for the PR workspace. If the folder is not a Git repo yet, ArchHarness will clone it for you."
    : "Enter the path to the local folder for the PR workspace. If the folder is not a Git repo yet, ArchHarness will clone it for you.";
}

function setReviewPrFolderHint(message = null) {
  const hintEl = document.getElementById("review-pr-folder-hint");
  if (!hintEl) {
    return;
  }

  hintEl.textContent = message || getReviewPrFolderBaseHint();
}

function getReviewPrSourceBranch(pr = reviewPrState.selectedPr) {
  return pr?.SourceBranch || pr?.sourceBranch || "";
}

function getReviewPrTargetBranch(pr = reviewPrState.selectedPr) {
  return pr?.TargetBranch || pr?.targetBranch || "";
}

function getReviewPrId(pr = reviewPrState.selectedPr) {
  return String(pr?.Id || pr?.id || pr?.PullRequestId || pr?.pullRequestId || "").trim();
}

function buildReviewPrDisplayName(pr, folderPath) {
  const repositoryName = pr?.RepositoryName || pr?.repositoryName || "Repository";
  const sourceBranch = getReviewPrSourceBranch(pr);
  if (repositoryName && sourceBranch) {
    return `${repositoryName} (${sourceBranch})`;
  }

  return repositoryName || summarizeWorkspacePath(folderPath) || "PR workspace";
}

async function ensureReviewPrProject() {
  const folderPath = reviewPrState.folderPath.trim();
  const pr = reviewPrState.selectedPr;
  const payload = {
    displayName: buildReviewPrDisplayName(pr, folderPath),
    workspacePath: folderPath,
    workspaceMode: "existing-git",
    permissionHandlerMode: elements.permissionMode.value || state.settings?.defaults?.permissionHandlerMode || "approve-all",
    architectureReviewMode: false,
    architectureReviewPrompt: null
  };

  const project = await requestJson("/api/projects", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload)
  });

  await requestJson(`/api/projects/${encodeURIComponent(project.projectId)}/source-control`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      providerName: reviewPrState.selectedProvider?.displayName || null,
      projectName: pr?.ProjectName || pr?.projectName || null,
      repositoryName: pr?.RepositoryName || pr?.repositoryName || null
    })
  });

  reviewPrState.projectId = project.projectId;
  return project;
}

async function finalizeReviewPrWorkspace(projectId) {
  reviewPrState.projectId = projectId;
  state.activeProjectId = projectId;
  await loadProjects();
  showReviewPrStep(3);
  await loadPrFiles();
}

async function prepareReviewPrWorkspace() {
  if (reviewPrState.isPreparingWorkspace) {
    return false;
  }

  const branchName = getReviewPrSourceBranch();
  if (!branchName) {
    throw new Error("The selected pull request does not include a source branch.");
  }

  reviewPrState.isPreparingWorkspace = true;
  setReviewPrFolderHint("Preparing the PR workspace...");
  updateReviewPrNavigation();

  try {
    const project = await ensureReviewPrProject();
    state.activeProjectId = project.projectId;

    const branchInfo = await requestJson(`/api/projects/${encodeURIComponent(project.projectId)}/branch`);
    applyProjectBranchInfo(project.projectId, branchInfo);

    if (!branchInfo?.isGitRepository) {
      setReviewPrFolderHint(`Cloning ${prSummaryLabel(reviewPrState.selectedPr)} into the selected folder...`);
      const cloneResponse = await requestJson(`/api/projects/${encodeURIComponent(project.projectId)}/git/clone`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ branchName })
      });

      applyProjectBranchInfo(project.projectId, cloneResponse);
      await finalizeReviewPrWorkspace(project.projectId);
      return true;
    }

    const workingTreeStatus = await requestJson(`/api/projects/${encodeURIComponent(project.projectId)}/git/changes`);
    const currentBranch = branchInfo?.currentBranch || null;
    const needsBranchSwitch = !currentBranch || !equalIgnoringCase(currentBranch, branchName);
    if (workingTreeStatus?.hasChanges) {
      reviewPrState.isPreparingWorkspace = false;
      updateReviewPrNavigation();
      await openGitChangeReview(
        project.projectId,
        needsBranchSwitch ? branchName : null,
        branchInfo,
        needsBranchSwitch
          ? {
              onCompleted: async () => {
                await finalizeReviewPrWorkspace(project.projectId);
              },
              onClosed: () => {
                openModal("review-pr-modal");
                showReviewPrStep(2);
                renderFolderStep();
              }
            }
          : {
              onClosed: () => {
                openModal("review-pr-modal");
                void finalizeReviewPrWorkspace(project.projectId);
              }
            }
      );
      return false;
    }

    if (currentBranch && !equalIgnoringCase(currentBranch, branchName)) {
      const confirmMessage = `Switch ${project.displayName} from ${currentBranch} to ${branchName} to review this pull request?`;
      if (!globalThis.confirm(confirmMessage)) {
        return false;
      }

      const switched = await handleWorkspaceBranchSelection(project.projectId, branchName, {
        onSucceeded: async () => {
          await finalizeReviewPrWorkspace(project.projectId);
        },
        onReviewClosed: () => {
          openModal("review-pr-modal");
          showReviewPrStep(2);
          renderFolderStep();
        }
      });
      return switched;
    }

    await finalizeReviewPrWorkspace(project.projectId);
    return true;
  } finally {
    reviewPrState.isPreparingWorkspace = false;
    setReviewPrFolderHint();
    updateReviewPrNavigation();
  }
}

function equalIgnoringCase(left, right) {
  return String(left || "").localeCompare(String(right || ""), undefined, { sensitivity: "accent" }) === 0;
}

function buildPullRequestReviewPrompt() {
  const pr = reviewPrState.selectedPr;
  const title = pr?.Title || pr?.title || "Pull request";
  const pullRequestId = getReviewPrId(pr) || "unknown";
  const sourceBranch = getReviewPrSourceBranch(pr);
  const targetBranch = getReviewPrTargetBranch(pr);
  const changedFiles = reviewPrState.prFiles
    .map(file => file.Path || file.path || file.FileName || file.fileName || "")
    .filter(Boolean)
    .slice(0, 200);

  const promptLines = [
    `Review pull request #${pullRequestId}: ${title}.`,
    sourceBranch ? `Source branch: ${sourceBranch}.` : "",
    targetBranch ? `Target branch: ${targetBranch}.` : "",
    "Focus on bugs, behavioral regressions, security issues, and missing tests.",
    changedFiles.length > 0 ? "Prioritize the files changed in this PR:" : ""
  ].filter(Boolean);

  if (changedFiles.length > 0) {
    changedFiles.forEach(path => {
      promptLines.push(`- ${path}`);
    });
  }

  return promptLines.join("\n");
}

async function startPullRequestReview() {
  if (reviewPrState.isStartingReview) {
    return;
  }

  const projectId = reviewPrState.projectId;
  const project = state.projects.find(candidate => candidate.projectId === projectId)
    || (await requestJson("/api/projects?maxRunsPerProject=24")).find(candidate => candidate.projectId === projectId);
  if (!project) {
    throw new Error("The PR workspace project could not be loaded.");
  }

  reviewPrState.isStartingReview = true;
  updateReviewPrNavigation();

  try {
    setSelectValue(elements.runMode, "architecture-review");
    setSelectValue(elements.architectureReviewPreset, "focused-review");
    renderComposerState();

    await submitRunRequest({
      taskPrompt: "",
      workspacePath: project.workspacePath,
      workspaceMode: project.workspaceMode || "existing-git",
      workflow: "architecture-loop",
      projectName: project.displayName,
      projectId: project.projectId,
      modelOverrides: null,
      buildCommand: null,
      permissionHandlerMode: elements.permissionMode.value || project.permissionHandlerMode,
      reviewLoopAgents: state.bootstrap?.reviewLoopAgents || {
        codingStyleEnabled: true,
        securityEnabled: true,
        architectureEnabled: true
      },
      architectureLoopMode: true,
      architectureLoopPrompt: buildPullRequestArchitecturePrompt(project),
      runTitle: `PR #${getReviewPrId() || ""} architecture review`.trim()
    });

    closeModal();
  } finally {
    reviewPrState.isStartingReview = false;
    updateReviewPrNavigation();
  }
}

function prSummaryLabel(pr) {
  return pr?.RepositoryName || pr?.repositoryName || "the repository";
}

function renderProviderPicker() {
  const list = document.getElementById("review-pr-provider-list");
  list.replaceChildren();

  reviewPrState.providers.forEach(provider => {
    const btn = document.createElement("button");
    btn.type = "button";
    btn.className = "provider-picker-item";
    const displayName = provider.displayName || "";
    const providerTypeLabel = getProviderMetaByType(provider.providerType)?.label || "";
    btn.textContent = providerTypeLabel ? `${displayName} · ${providerTypeLabel}` : displayName;
    btn.classList.toggle("selected", provider === reviewPrState.selectedProvider);

    btn.addEventListener("click", () => {
      if (reviewPrState.selectedProvider === provider && reviewPrState.step !== 0) {
        return;
      }

      reviewPrState.selectedProvider = provider;
      list.querySelectorAll(".provider-picker-item").forEach(b => b.classList.remove("selected"));
      btn.classList.add("selected");
      showReviewPrStep(1);
      void loadPullRequests();
    });

    list.append(btn);
  });
}

async function loadPullRequests() {
  const loadingEl = document.getElementById("review-pr-list-loading");
  const listEl = document.getElementById("review-pr-list");
  const nextBtn = document.getElementById("review-pr-next-button");

  abortPullRequestStream();
  loadingEl.classList.remove("hidden");
  listEl.replaceChildren();
  reviewPrState.allPullRequests = [];
  reviewPrState.pullRequests = [];
  reviewPrState.selectedProjects = [];
  reviewPrState.selectedRepositories = [];
  reviewPrState.selectedAuthors = [];
  reviewPrState.isPullRequestStreamLoading = true;
  reviewPrState.isPullRequestStreamComplete = false;
  reviewPrState.pullRequestError = "";
  reviewPrState.selectedPr = null;
  reviewPrState.prFiles = [];
  reviewPrState.prFilesError = "";
  if (nextBtn) nextBtn.disabled = true;

  const providerName = normalizeReviewLookupValue(reviewPrState.selectedProvider?.displayName, REVIEW_PROVIDER_NAME_MAX_LENGTH);

  renderPullRequestFilters();
  renderPullRequestList();
  renderPullRequestLoadingState();

  if (!providerName) {
    reviewPrState.isPullRequestStreamLoading = false;
    reviewPrState.isPullRequestStreamComplete = true;
    renderPullRequestLoadingState();
    return;
  }

  const streamController = new AbortController();
  reviewPrState.pullRequestStreamController = streamController;

  try {
    await requestEventStream(`/api/providers/${encodeURIComponent(providerName)}/pullrequests/stream`, {
      headers: {
        Accept: "text/event-stream"
      },
      signal: streamController.signal,
      onEvent: ({ event, data }) => {
        if (streamController.signal.aborted) {
          return;
        }

        if (event === "batch") {
          appendPullRequestBatch(Array.isArray(data?.pullRequests) ? data.pullRequests : []);
          renderPullRequestFilters();
          applyPullRequestFilters();
        } else if (event === "error") {
          reviewPrState.pullRequestError = data?.error || "Failed to load pull requests.";
          renderPullRequestList();
        } else if (event === "completed") {
          reviewPrState.isPullRequestStreamComplete = true;
        }

        renderPullRequestLoadingState();
      }
    });
  } catch (error) {
    if (streamController.signal.aborted) {
      return;
    }

    reviewPrState.pullRequestError = error?.message || "Failed to load pull requests.";
  } finally {
    if (reviewPrState.pullRequestStreamController === streamController) {
      reviewPrState.pullRequestStreamController = null;
      reviewPrState.isPullRequestStreamLoading = false;
      renderPullRequestLoadingState();
      renderPullRequestFilters();
      applyPullRequestFilters();
    }
  }
}

function abortPullRequestStream() {
  if (reviewPrState.pullRequestStreamController) {
    reviewPrState.pullRequestStreamController.abort();
    reviewPrState.pullRequestStreamController = null;
  }

  reviewPrState.isPullRequestStreamLoading = false;
}

function getPullRequestKey(pr) {
  const id = String(pr?.Id || pr?.id || pr?.PullRequestId || pr?.pullRequestId || "").trim();
  const project = getPullRequestFieldValue(pr, "ProjectName", "projectName");
  const repository = getPullRequestFieldValue(pr, "RepositoryName", "repositoryName");
  return `${project}::${repository}::${id}`;
}

function appendPullRequestBatch(batch) {
  if (!Array.isArray(batch) || batch.length === 0) {
    return;
  }

  const existingKeys = new Set(reviewPrState.allPullRequests.map(getPullRequestKey));
  batch.forEach(pr => {
    const key = getPullRequestKey(pr);
    if (!existingKeys.has(key)) {
      existingKeys.add(key);
      reviewPrState.allPullRequests.push(pr);
    }
  });
}

function renderPullRequestLoadingState() {
  const loadingEl = document.getElementById("review-pr-list-loading");
  if (!loadingEl) {
    return;
  }

  if (!reviewPrState.isPullRequestStreamLoading) {
    loadingEl.classList.add("hidden");
    loadingEl.textContent = "Loading…";
    return;
  }

  const loadedCount = reviewPrState.allPullRequests.length;
  loadingEl.textContent = loadedCount > 0
    ? `Loading pull requests… ${loadedCount} loaded so far.`
    : "Loading pull requests…";
  loadingEl.classList.remove("hidden");
}

function getPullRequestFieldValue(pr, preferredKey, fallbackKey) {
  return normalizeReviewLookupValue(pr?.[preferredKey] ?? pr?.[fallbackKey] ?? "");
}

function getUniquePullRequestValues(getValue) {
  return [...new Set(
    reviewPrState.allPullRequests
      .map(pr => getValue(pr))
      .filter(Boolean)
  )].sort((left, right) => left.localeCompare(right));
}

function getPullRequestFilterSelection(filterKey) {
  if (filterKey === "project") {
    return reviewPrState.selectedProjects;
  }

  if (filterKey === "repository") {
    return reviewPrState.selectedRepositories;
  }

  return reviewPrState.selectedAuthors;
}

function setPullRequestFilterSelection(filterKey, selectedValues) {
  if (filterKey === "project") {
    reviewPrState.selectedProjects = selectedValues;
    return;
  }

  if (filterKey === "repository") {
    reviewPrState.selectedRepositories = selectedValues;
    return;
  }

  reviewPrState.selectedAuthors = selectedValues;
}

function togglePullRequestFilterValue(filterKey, value) {
  const currentSelection = getPullRequestFilterSelection(filterKey);
  const nextSelection = currentSelection.includes(value)
    ? currentSelection.filter(selectedValue => selectedValue !== value)
    : [...currentSelection, value];

  setPullRequestFilterSelection(filterKey, nextSelection);
  renderPullRequestFilters();
  applyPullRequestFilters();
}

function clearPullRequestFilter(filterKey) {
  setPullRequestFilterSelection(filterKey, []);
  renderPullRequestFilters();
  applyPullRequestFilters();
}

function renderPullRequestFilterChips(containerEl, values, selectedValues, filterKey) {
  if (!containerEl) {
    return;
  }

  containerEl.replaceChildren();
  if (values.length === 0) {
    const emptyEl = document.createElement("span");
    emptyEl.className = "filter-chip-empty";
    emptyEl.textContent = reviewPrState.isPullRequestStreamLoading ? "Loading options…" : "No values available.";
    containerEl.append(emptyEl);
    return;
  }

  const orderedValues = [
    ...values.filter(value => selectedValues.includes(value)),
    ...values.filter(value => !selectedValues.includes(value))
  ];

  orderedValues.forEach(value => {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "filter-chip";
    button.textContent = value;
    button.setAttribute("aria-pressed", selectedValues.includes(value) ? "true" : "false");
    button.classList.toggle("selected", selectedValues.includes(value));
    button.addEventListener("click", () => {
      togglePullRequestFilterValue(filterKey, value);
    });
    containerEl.append(button);
  });
}

function renderPullRequestFilters() {
  const projectContainer = document.getElementById("pr-filter-project");
  const repositoryContainer = document.getElementById("pr-filter-repo");
  const authorContainer = document.getElementById("pr-filter-author");
  const projectClearButton = document.getElementById("pr-filter-project-clear");
  const repositoryClearButton = document.getElementById("pr-filter-repo-clear");
  const authorClearButton = document.getElementById("pr-filter-author-clear");

  const projectValues = getUniquePullRequestValues(pr => getPullRequestFieldValue(pr, "ProjectName", "projectName"));
  const repositoryValues = getUniquePullRequestValues(pr => getPullRequestFieldValue(pr, "RepositoryName", "repositoryName"));
  const authorValues = getUniquePullRequestValues(pr => getPullRequestFieldValue(pr, "Author", "author"));

  reviewPrState.selectedProjects = reviewPrState.selectedProjects.filter(value => projectValues.includes(value));
  reviewPrState.selectedRepositories = reviewPrState.selectedRepositories.filter(value => repositoryValues.includes(value));
  reviewPrState.selectedAuthors = reviewPrState.selectedAuthors.filter(value => authorValues.includes(value));

  renderPullRequestFilterChips(projectContainer, projectValues, reviewPrState.selectedProjects, "project");
  renderPullRequestFilterChips(repositoryContainer, repositoryValues, reviewPrState.selectedRepositories, "repository");
  renderPullRequestFilterChips(authorContainer, authorValues, reviewPrState.selectedAuthors, "author");

  if (projectClearButton) {
    projectClearButton.disabled = reviewPrState.selectedProjects.length === 0;
  }

  if (repositoryClearButton) {
    repositoryClearButton.disabled = reviewPrState.selectedRepositories.length === 0;
  }

  if (authorClearButton) {
    authorClearButton.disabled = reviewPrState.selectedAuthors.length === 0;
  }
}

function applyPullRequestFilters() {
  const matchesSelectedValues = (selectedValues, value) => selectedValues.length === 0 || selectedValues.includes(value);

  reviewPrState.pullRequests = reviewPrState.allPullRequests.filter(pr => {
    const projectValue = getPullRequestFieldValue(pr, "ProjectName", "projectName");
    const repositoryValue = getPullRequestFieldValue(pr, "RepositoryName", "repositoryName");
    const authorValue = getPullRequestFieldValue(pr, "Author", "author");

    return matchesSelectedValues(reviewPrState.selectedProjects, projectValue)
      && matchesSelectedValues(reviewPrState.selectedRepositories, repositoryValue)
      && matchesSelectedValues(reviewPrState.selectedAuthors, authorValue);
  });

  if (reviewPrState.selectedPr && !reviewPrState.pullRequests.includes(reviewPrState.selectedPr)) {
    reviewPrState.selectedPr = null;
  }

  renderPullRequestList();
}

function renderPullRequestList() {
  const errorEl = document.getElementById("review-pr-list-error");
  const emptyEl = document.getElementById("review-pr-list-empty");
  const listEl = document.getElementById("review-pr-list");
  const nextBtn = document.getElementById("review-pr-next-button");
  listEl.replaceChildren();
  if (nextBtn) nextBtn.disabled = !reviewPrState.selectedPr;

  if (reviewPrState.pullRequestError) {
    errorEl.textContent = reviewPrState.pullRequestError;
    errorEl.classList.remove("hidden");
  } else {
    errorEl.classList.add("hidden");
    errorEl.textContent = "";
  }

  if (reviewPrState.pullRequests.length === 0) {
    if (reviewPrState.isPullRequestStreamLoading && reviewPrState.allPullRequests.length === 0 && !reviewPrState.pullRequestError) {
      emptyEl.classList.add("hidden");
      return;
    }

    const hasActiveFilters = reviewPrState.selectedProjects.length > 0
      || reviewPrState.selectedRepositories.length > 0
      || reviewPrState.selectedAuthors.length > 0;
    emptyEl.textContent = hasActiveFilters ? "No pull requests match the selected filters." : "No pull requests found.";
    emptyEl.classList.remove("hidden");
    return;
  }
  emptyEl.classList.add("hidden");

  reviewPrState.pullRequests.forEach(pr => {
    const li = document.createElement("li");
    li.className = "pr-list-item";
    li.classList.toggle("selected", pr === reviewPrState.selectedPr);

    const titleEl = document.createElement("span");
    titleEl.className = "pr-title";
    titleEl.textContent = pr.Title || pr.title || "";

    const metaEl = document.createElement("span");
    metaEl.className = "pr-meta";
    const parts = [
      pr.Author || pr.author || "",
      pr.SourceBranch || pr.sourceBranch || "",
      (pr.TargetBranch || pr.targetBranch) ? `→ ${pr.TargetBranch || pr.targetBranch}` : "",
      pr.ProjectName || pr.projectName || "",
      pr.RepositoryName || pr.repositoryName || ""
    ].filter(Boolean);
    metaEl.textContent = parts.join(" · ");

    li.append(titleEl, metaEl);
    li.addEventListener("click", () => {
      reviewPrState.selectedPr = pr;
      listEl.querySelectorAll(".pr-list-item").forEach(item => item.classList.remove("selected"));
      li.classList.add("selected");
      if (nextBtn) nextBtn.disabled = false;
    });

    listEl.append(li);
  });
}

function renderFolderStep() {
  const pr = reviewPrState.selectedPr;
  const summaryEl = document.getElementById("review-pr-selected-pr-summary");
  const browseButton = document.getElementById("review-pr-pick-folder");
  summaryEl.replaceChildren();

  const titleEl = document.createElement("strong");
  titleEl.className = "pr-title";
  titleEl.textContent = pr.Title || pr.title || "";

  const prUrl = pr.Url || pr.url || "";
  if (prUrl) {
    const link = document.createElement("a");
    link.href = prUrl;
    link.target = "_blank";
    link.rel = "noopener noreferrer";
    link.textContent = "View PR ↗";
    summaryEl.append(titleEl, document.createTextNode(" "), link);
  } else {
    summaryEl.append(titleEl);
  }

  const folderInput = document.getElementById("review-pr-folder-path");
  folderInput.value = reviewPrState.folderPath;
  setReviewPrFolderHint();
  if (browseButton) {
    browseButton.disabled = !desktopBridge?.selectFolder;
  }

  folderInput.oninput = () => {
    reviewPrState.folderPath = folderInput.value;
    reviewPrState.projectId = null;
    reviewPrState.prFiles = [];
    reviewPrState.prFilesError = "";
    updateReviewPrNavigation();
  };

  updateReviewPrNavigation();
}

async function loadPrFiles() {
  const pr = reviewPrState.selectedPr;
  const providerName = normalizeReviewLookupValue(reviewPrState.selectedProvider?.displayName, REVIEW_PROVIDER_NAME_MAX_LENGTH);
  const prId = normalizeReviewPullRequestId(pr.Id ?? pr.id ?? pr.PullRequestId ?? pr.pullRequestId ?? "");
  const projectName = normalizeReviewLookupValue(pr.ProjectName ?? pr.projectName ?? "");
  const repositoryName = normalizeReviewLookupValue(pr.RepositoryName ?? pr.repositoryName ?? "");
  const params = new URLSearchParams();
  if (projectName) params.set("project", projectName);
  if (repositoryName) params.set("repository", repositoryName);

  if (!providerName || !prId) {
    reviewPrState.prFiles = [];
    reviewPrState.prFilesError = "Select a valid provider and pull request to load changed files.";
    renderConfirmStep();
    return;
  }

  try {
    const qs = params.toString();
    const files = await requestJson(`/api/providers/${encodeURIComponent(providerName)}/pullrequests/${encodeURIComponent(prId)}/files${qs ? "?" + qs : ""}`);
    reviewPrState.prFiles = Array.isArray(files) ? files : [];
    reviewPrState.prFilesError = "";
  } catch (error) {
    reviewPrState.prFiles = [];
    reviewPrState.prFilesError = error?.message || "Failed to load changed files for this pull request.";
  }

  renderConfirmStep();
}

function renderConfirmStep() {
  const pr = reviewPrState.selectedPr;
  const summaryEl = document.getElementById("review-pr-confirm-summary");
  const statusEl = document.getElementById("review-pr-file-status");
  summaryEl.replaceChildren();

  const titleEl = document.createElement("strong");
  titleEl.textContent = pr.Title || pr.title || "";

  const metaEl = document.createElement("p");
  metaEl.className = "pr-meta";
  const author = pr.Author || pr.author || "";
  const sourceBranch = pr.SourceBranch || pr.sourceBranch || "";
  const targetBranch = pr.TargetBranch || pr.targetBranch || "";
  const parts = [author, sourceBranch, targetBranch ? `→ ${targetBranch}` : ""].filter(Boolean);
  metaEl.textContent = parts.join(" · ");

  summaryEl.append(titleEl, metaEl);

  const fileList = document.getElementById("review-pr-file-list");
  fileList.replaceChildren();

  if (statusEl) {
    if (reviewPrState.prFilesError) {
      statusEl.textContent = reviewPrState.prFilesError;
      statusEl.classList.remove("hidden");
    } else if (reviewPrState.prFiles.length === 0) {
      statusEl.textContent = "No changed files were returned for this pull request.";
      statusEl.classList.remove("hidden");
    } else {
      statusEl.textContent = "";
      statusEl.classList.add("hidden");
    }
  }

  reviewPrState.prFiles.forEach(file => {
    const li = document.createElement("li");
    li.className = "pr-file-item";

    const pathEl = document.createElement("span");
    pathEl.textContent = file.Path || file.path || file.FileName || file.fileName || "";

    const rawType = file.ChangeType || file.changeType || "modified";
    const changeType = String(rawType).toLowerCase();
    const badge = document.createElement("span");
    badge.className = `pr-file-badge pr-badge-${changeType}`;
    badge.textContent = changeType;

    li.append(pathEl, badge);
    fileList.append(li);
  });

  updateReviewPrNavigation();
}

async function handleReviewPrNext() {
  const step = reviewPrState.step;
  if (step === 0) {
    showReviewPrStep(1);
    void loadPullRequests();
  } else if (step === 1) {
    showReviewPrStep(2);
    renderFolderStep();
  } else if (step === 2) {
    reviewPrState.folderPath = document.getElementById("review-pr-folder-path").value;
    await prepareReviewPrWorkspace();
  } else if (step === 3) {
    await startPullRequestReview();
  }
}

function handleReviewPrBack() {
  showReviewPrStep(reviewPrState.step - 1);

  if (reviewPrState.step === 2) {
    renderFolderStep();
  } else if (reviewPrState.step === 3) {
    renderConfirmStep();
  }
}

// =================== End Review PR ===================

function handleVisibilityChange() {
  if (document.hidden) {
    clearPendingInteractionPoll();
    abortPendingInteractionPoll();
    return;
  }

  void pollPendingInteraction();
}

function attachHandlers() {
  elements.newProjectButton.addEventListener("click", () => openModal("new-project-modal"));
  elements.pickProjectFolder.addEventListener("click", () => {
    void pickProjectFolder().catch(error => {
      elements.projectPickerNote.textContent = `Folder selection failed: ${error.message}`;
    });
  });
  elements.reviewPrPickFolder.addEventListener("click", () => {
    void pickReviewPrFolder().catch(error => {
      const hintEl = document.getElementById("review-pr-folder-hint");
      if (hintEl) {
        hintEl.textContent = `Folder selection failed: ${error.message}`;
      }
    });
  });
  elements.settingsButton.addEventListener("click", () => {
    renderSettingsForm();
    applySettingsDefaults();
    switchSettingsTab("agent-settings");
    closeProviderSetup();
    void loadProviders();
    openModal("settings-modal");
  });
  elements.streamSections.addEventListener("scroll", () => {
    const el = elements.streamSections;
    const atBottom = el.scrollHeight - el.scrollTop - el.clientHeight < 40;
    state.streamAutoScroll = atBottom;
  });
  elements.startRun.addEventListener("click", () => startRun().catch(error => {
    console.error("Run submission failed:", error);
  }));
  elements.cancelRun.addEventListener("click", () => cancelRun().catch(error => {
    console.error("Cancel failed:", error);
  }));
  elements.resumeRun?.addEventListener("click", () => resumeSelectedRun().catch(error => {
    console.error("Resume failed:", error);
    renderRunDetailsActions();
  }));
  elements.taskPrompt.addEventListener("input", () => {
    saveShellState();
    renderComposerState();
  });
  [elements.runMode, elements.permissionMode, elements.architectureReviewPreset].forEach(control => {
    control.addEventListener("input", () => {
      saveShellState();
      renderComposerState();
    });
    control.addEventListener("change", () => {
      saveShellState();
      renderComposerState();
    });
  });

  document.querySelectorAll("[data-close-modal]").forEach(button => {
    button.addEventListener("click", closeModal);
  });
  elements.modalBackdrop.addEventListener("click", closeModal);
  document.querySelectorAll(".settings-tab").forEach(btn => {
    btn.addEventListener("click", () => switchSettingsTab(btn.dataset.tab));
  });
  elements.btnAddProvider.addEventListener("click", () => openProviderSetup());
  elements.btnCancelProvider.addEventListener("click", closeProviderSetup);
  elements.btnTestProvider.addEventListener("click", () => {
    void testProviderConnection().catch(error => console.error("Provider test failed:", error));
  });
  elements.btnSaveProvider.addEventListener("click", () => {
    void saveProvider().catch(error => console.error("Save provider failed:", error));
  });
  elements.providerTypeRadios.forEach(radio => {
    radio.addEventListener("change", onProviderSetupTypeChange);
  });
  [elements.providerDisplayName, elements.providerServerUrl, elements.providerOrg, elements.providerPat].forEach(input => {
    input.addEventListener("input", () => {
      state.providerConnectionTested = false;
      setProviderStatus();
    });
  });
  elements.providerPatToggle.addEventListener("click", () => {
    setProviderPatMasked(elements.providerPat.type !== "password");
  });
  elements.newProjectForm.addEventListener("submit", event => {
    void createProject(event).catch(error => {
      console.error("Project creation failed:", error);
    });
  });
  elements.settingsForm.addEventListener("submit", event => {
    void saveSettings(event).catch(error => {
      console.error("Saving settings failed:", error);
    });
  });

  document.addEventListener("visibilitychange", handleVisibilityChange);
  document.addEventListener("click", event => {
    if (!elements.workspaceBranchWrap.contains(event.target)) {
      closeWorkspaceBranchMenu();
    }

    const composerDropdownClicked = getComposerDropdownConfigs().some(config => config.wrap.contains(event.target));
    if (!composerDropdownClicked) {
      closeComposerDropdowns();
    }
  });
  document.addEventListener("keydown", event => {
    if (event.key === "Escape") {
      closeWorkspaceBranchMenu();
      closeComposerDropdowns();
    }
  });

  document.getElementById("review-pr-button").addEventListener("click", () => {
    void openReviewPrModal();
  });
  elements.workspaceBranchButton.addEventListener("click", event => {
    event.stopPropagation();
    toggleWorkspaceBranchMenu();
  });
  elements.runModeButton.addEventListener("click", event => {
    event.stopPropagation();
    toggleComposerDropdown("run-mode");
  });
  elements.permissionModeButton.addEventListener("click", event => {
    event.stopPropagation();
    toggleComposerDropdown("permission-mode");
  });
  elements.architectureReviewPresetButton.addEventListener("click", event => {
    event.stopPropagation();
    toggleComposerDropdown("architecture-review-preset");
  });
  elements.gitChangesStashButton.addEventListener("click", () => {
    void stashGitChangesAndContinue().catch(error => {
      console.error("Stash and switch failed:", error);
    });
  });
  document.getElementById("review-pr-close-button").addEventListener("click", closeModal);
  document.getElementById("review-pr-back-button").addEventListener("click", handleReviewPrBack);
  elements.reviewPrGoButton.addEventListener("click", () => {
    void startPullRequestReview().catch(error => {
      setReviewPrFolderHint(error?.message || "Failed to start the PR architecture review.");
      reviewPrState.isStartingReview = false;
      updateReviewPrNavigation();
      console.error("PR architecture review failed:", error);
    });
  });
  document.getElementById("review-pr-next-button").addEventListener("click", () => {
    void handleReviewPrNext().catch(error => {
      setReviewPrFolderHint(error?.message || "Failed to prepare the PR workspace.");
      reviewPrState.isPreparingWorkspace = false;
      reviewPrState.isStartingReview = false;
      updateReviewPrNavigation();
      console.error("PR review step failed:", error);
    });
  });
  document.getElementById("pr-filter-project-clear").addEventListener("click", () => clearPullRequestFilter("project"));
  document.getElementById("pr-filter-repo-clear").addEventListener("click", () => clearPullRequestFilter("repository"));
  document.getElementById("pr-filter-author-clear").addEventListener("click", () => clearPullRequestFilter("author"));
}

async function init() {
  applyDesktopChrome();
  attachHandlers();
  restoreShellState();
  clearLegacyAutofillPrompt();
  await Promise.all([loadBootstrap(), warmModelDiscovery()]);
  await loadSettings();
  await loadProjects();
  await refreshActiveRun();
  await loadSelectedRunStream();
  renderInlineInteraction();
  connectEventStream();
  await pollPendingInteraction();
}

globalThis.addEventListener("beforeunload", () => {
  state.isUnloading = true;
  closeEventStream();
  clearPendingInteractionPoll();
  abortPendingInteractionPoll();
});

try {
  await init();
} catch (error) {
  console.error("Initialization failed:", error);
}
