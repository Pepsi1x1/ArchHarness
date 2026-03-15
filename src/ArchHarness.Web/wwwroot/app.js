const state = {
  bootstrap: null,
  settings: null,
  models: [],
  projects: [],
  activeProjectId: null,
  activeRunId: null,
  activeRun: null,
  artifacts: [],
  selectedArtifactPath: null,
  streamSections: {},
  streamOrder: [],
  streamAutoScroll: true,
  eventSource: null,
  pendingInteraction: null,
  interactionPollHandle: null,
  pendingInteractionAbortController: null,
  pendingInteractionInFlight: false,
  isUnloading: false,
  openModalId: null,
  expandedProjectIds: new Set(),
  seenRunIds: new Set()
};

const desktopBridge = window.archHarnessDesktop || null;

const STORAGE_KEY = "archharness.web.shell-state";
const IDLE_INTERACTION_POLL_MS = 5000;
const ACTIVE_INTERACTION_POLL_MS = 400;
const STREAM_RENDER_DELAY_MS = 140;
const LEGACY_AUTOFILL_PROMPTS = [
  "Implement requested change",
  "Run coding style, security, and architecture review loop for the existing workspace and apply required remediation."
];
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
  refreshProjects: document.getElementById("refresh-projects"),
  newProjectButton: document.getElementById("new-project-button"),
  settingsButton: document.getElementById("settings-button"),
  projectList: document.getElementById("project-list"),
  workspaceTitle: document.getElementById("workspace-title"),
  eventStreamState: null,
  streamSummary: null,
  streamEmpty: document.getElementById("stream-empty"),
  streamSections: document.getElementById("stream-sections"),
  inlineInteraction: document.getElementById("inline-interaction"),

  taskPrompt: document.getElementById("task-prompt"),
  runMode: document.getElementById("run-mode"),
  permissionMode: document.getElementById("permission-mode"),
  architectureReviewChip: document.getElementById("architecture-review-chip"),
  architectureReviewPreset: document.getElementById("architecture-review-preset"),
  startRun: document.getElementById("start-run"),
  cancelRun: document.getElementById("cancel-run"),
  modalBackdrop: document.getElementById("modal-backdrop"),
  newProjectModal: document.getElementById("new-project-modal"),
  newProjectForm: document.getElementById("new-project-form"),
  newProjectName: document.getElementById("new-project-name"),
  newProjectPath: document.getElementById("new-project-path"),
  pickProjectFolder: document.getElementById("pick-project-folder"),
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
  artifactList: document.getElementById("artifact-list"),
  artifactPreview: document.getElementById("artifact-preview"),
  artifactSummary: document.getElementById("artifact-summary"),
  projectTemplate: document.getElementById("project-template"),
  runTemplate: document.getElementById("run-template"),
  artifactTemplate: document.getElementById("artifact-template")
};

async function requestJson(url, options) {
  const response = await fetch(url, options);
  if (response.status === 204) {
    return null;
  }

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `Request failed with status ${response.status}`);
  }

  return response.json();
}

function saveShellState() {
  const payload = {
    activeProjectId: state.activeProjectId,
    taskPrompt: elements.taskPrompt.value,
    runMode: elements.runMode.value,
    permissionMode: elements.permissionMode.value,
    architectureReviewPreset: elements.architectureReviewPreset.value,
    seenRunIds: [...state.seenRunIds]
  };
  window.localStorage.setItem(STORAGE_KEY, JSON.stringify(payload));
}

function restoreShellState() {
  const raw = window.localStorage.getItem(STORAGE_KEY);
  if (!raw) {
    return;
  }

  try {
    const saved = JSON.parse(raw);
    state.activeProjectId = saved.activeProjectId || null;
    state.seenRunIds = new Set(Array.isArray(saved.seenRunIds) ? saved.seenRunIds : []);
    elements.taskPrompt.value = saved.taskPrompt || "";
    setSelectValue(elements.runMode, saved.runMode);
    setSelectValue(elements.permissionMode, saved.permissionMode);
    setSelectValue(elements.architectureReviewPreset, saved.architectureReviewPreset);
  } catch {
    window.localStorage.removeItem(STORAGE_KEY);
  }
}

function clearLegacyAutofillPrompt() {
  if (LEGACY_AUTOFILL_PROMPTS.includes(elements.taskPrompt.value.trim())) {
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
  if (isNaN(date)) return "";
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
  const normalized = String(path || "").replace(/\\/g, "/").replace(/\/$/, "");
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

// Sanitizes server-rendered HTML by stripping script elements and inline event handlers
// to defend against XSS even when the source is a trusted local backend.
function sanitizeHtml(html) {
  const doc = new DOMParser().parseFromString(html || "", "text/html");
  doc.querySelectorAll("script,iframe,object,embed,form,base,meta,svg,math,link[rel=import]").forEach(el => el.remove());
  doc.querySelectorAll("*").forEach(el => {
    for (const attr of [...el.attributes]) {
      if (attr.name.startsWith("on") || (attr.name === "href" && attr.value.trimStart().startsWith("javascript:"))) {
        el.removeAttribute(attr.name);
      }
    }
  });
  return doc.body.innerHTML;
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

function clearPendingInteractionPoll() {
  if (state.interactionPollHandle) {
    window.clearTimeout(state.interactionPollHandle);
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

  state.interactionPollHandle = window.setTimeout(() => {
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

function closeModal() {
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
  if (!desktopBridge?.selectFolder) {
    elements.projectPickerNote.textContent = "The system picker is only available in desktop mode.";
    return;
  }

  const selectedPath = await desktopBridge.selectFolder();
  if (!selectedPath) {
    return;
  }

  elements.newProjectPath.value = selectedPath;
  if (!elements.newProjectName.value.trim()) {
    const segments = selectedPath.replace(/\\/g, "/").split("/").filter(Boolean);
    elements.newProjectName.value = segments[segments.length - 1] || selectedPath;
  }
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

  if (!activeRun.isRunning && !state.isUnloading) {
    closeEventStream("idle");
  }

  renderTopbar();
}

function renderTopbar() {
  const activeProject = getActiveProject();
  const activeRun = state.activeRun;
  const runStatus = activeRun?.isRunning
    ? `Live run • ${activeRun.status || "running"}`
    : (state.pendingInteraction ? (state.pendingInteraction.kind === "permission" ? "Approval needed" : "Input needed") : "idle");

  elements.workspaceTitle.textContent = activeProject ? activeProject.displayName : "No project selected";
  renderComposerState();
}

function renderComposerState() {
  const activeProject = getActiveProject();
  const architectureMode = isArchitectureModeEnabled();
  elements.architectureReviewChip.classList.toggle("hidden", !architectureMode);
  elements.taskPrompt.placeholder = getPromptPlaceholder();
  elements.startRun.disabled = !activeProject || !elements.taskPrompt.value.trim();
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
          state.activeProjectId = project.projectId;
          state.activeRunId = run.runId;
          if (!state.activeRun?.isRunning || state.activeRun?.runId !== run.runId) {
            state.seenRunIds.add(run.runId);
          }
          syncComposerFromProject(project);
          saveShellState();
          renderProjects();
          renderTopbar();
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
      content: "",
      html: "",
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
    body.innerHTML = sanitizeHtml(section.html || `<pre>${escapeHtml(section.content || "Waiting for rendered markdown...")}</pre>`);

    details.append(summary, body);
    elements.streamSections.append(details);
  });

  scrollStreamToBottom();
  renderTopbar();
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
    window.clearTimeout(section.renderHandle);
  }

  section.renderHandle = window.setTimeout(() => {
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
  try {
    const response = await requestJson("/api/markdown/render", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ markdown: section.content })
    });

    if (!state.streamSections[agentId] || state.streamSections[agentId].renderVersion !== version) {
      return;
    }

    section.html = response.html || "<p>No output yet.</p>";
  } catch {
    if (!state.streamSections[agentId] || state.streamSections[agentId].renderVersion !== version) {
      return;
    }

    section.html = `<pre>${escapeHtml(section.content)}</pre>`;
  }

  const container = elements.streamSections.querySelector(`[data-agent-id="${CSS.escape(agentId)}"]`);
  if (container) {
    container.innerHTML = sanitizeHtml(section.html);
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

  const title = readEventField(entry, "title");
  const streamKind = readEventField(entry, "streamKind") || "assistant";
  const section = ensureStreamSection(agentId, agentRole, title);
  section.content += message;
  section.segmentCount += 1;
  section.updatedAt = readEventField(entry, "timestampUtc") || new Date().toISOString();
  section.streamKind = streamKind;

  renderStream();
  scheduleStreamRender(agentId);
}

function resetStream() {
  Object.values(state.streamSections).forEach(section => {
    if (section.renderHandle) {
      window.clearTimeout(section.renderHandle);
    }
  });
  state.streamSections = {};
  state.streamOrder = [];
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
  if (!state.activeProjectId || !state.projects.some(project => project.projectId === state.activeProjectId)) {
    state.activeProjectId = state.projects[0]?.projectId || null;
  }
  if (state.activeProjectId) {
    state.expandedProjectIds.add(state.activeProjectId);
  }

  if (!state.activeRunId && state.projects.length > 0) {
    state.activeRunId = state.projects[0].runs?.[0]?.runId || null;
  }

  syncComposerFromProject(getActiveProject());

  renderProjects();
  saveShellState();
}

async function loadSettings() {
  state.settings = await requestJson("/api/settings");
  state.models = (await requestJson("/api/models"))?.models || [];
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
  resetStream();
  showStreamStarting();
  const snapshot = await requestJson("/api/runs", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request)
  });

  state.activeRun = snapshot;
  renderActiveRun();
  connectEventStream();
  await loadProjects();
}

async function cancelRun() {
  state.activeRun = await requestJson("/api/runs/active", {
    method: "DELETE"
  });
  renderActiveRun();
}

async function refreshActiveRun() {
  state.activeRun = await requestJson("/api/runs/active");
  renderActiveRun();
  return state.activeRun;
}

function connectEventStream() {
  if (state.eventSource || !state.activeRun?.isRunning) {
    return;
  }

  const eventSource = new EventSource("/api/runs/active/events");
  state.eventSource = eventSource;

  const onEvent = async event => {
    const payload = JSON.parse(event.data);
    if ((readEventField(payload, "kind") || "") === "agent-delta") {
      recordStreamEvent(payload);
    }

    const snapshot = await refreshActiveRun();
    if (!snapshot?.isRunning) {
      closeEventStream("idle");
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
      window.setTimeout(connectEventStream, 1000);
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

async function openRunDetails(project, run) {
  state.activeProjectId = project.projectId;
  state.activeRunId = run.runId;
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
  elements.settingsButton.addEventListener("click", () => {
    renderSettingsForm();
    applySettingsDefaults();
    openModal("settings-modal");
  });
  elements.refreshProjects.addEventListener("click", () => loadProjects());

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
}

async function init() {
  attachHandlers();
  restoreShellState();
  clearLegacyAutofillPrompt();
  await Promise.all([loadBootstrap(), warmModelDiscovery()]);
  await loadSettings();
  await loadProjects();
  await refreshActiveRun();
  renderStream();
  renderInlineInteraction();
  connectEventStream();
  await pollPendingInteraction();
}

window.addEventListener("beforeunload", () => {
  state.isUnloading = true;
  closeEventStream();
  clearPendingInteractionPoll();
  abortPendingInteractionPoll();
});

init().catch(error => {
  console.error("Initialization failed:", error);
});