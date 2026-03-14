const state = {
  bootstrap: null,
  activeRun: null,
  selectedRunId: null,
  selectedArtifactPath: null,
  artifacts: [],
  recentRuns: [],
  eventSource: null,
  pendingInteraction: null,
  interactionPollHandle: null,
  pendingInteractionAbortController: null,
  pendingInteractionInFlight: false,
  isUnloading: false,
  timeline: []
};

const STORAGE_KEY = "archharness.web.form-state";
const IDLE_INTERACTION_POLL_MS = 5000;
const ACTIVE_INTERACTION_POLL_MS = 400;

const elements = {
  preflightTitle: document.getElementById("preflight-title"),
  preflightDetail: document.getElementById("preflight-detail"),
  activeRunStatus: document.getElementById("active-run-status"),
  activeRunDetail: document.getElementById("active-run-detail"),
  recentRuns: document.getElementById("recent-runs"),
  historyHint: document.getElementById("history-hint"),
  refreshHistory: document.getElementById("refresh-history"),
  startRun: document.getElementById("start-run"),
  cancelRun: document.getElementById("cancel-run"),
  generateSummary: document.getElementById("generate-summary"),
  setupSummary: document.getElementById("setup-summary"),
  setupMessage: document.getElementById("setup-message"),
  timeline: document.getElementById("timeline"),
  eventStreamState: document.getElementById("event-stream-state"),
  interactionCard: document.getElementById("interaction-card"),
  artifactList: document.getElementById("artifact-list"),
  artifactPreview: document.getElementById("artifact-preview"),
  artifactContext: document.getElementById("artifact-context"),
  runItemTemplate: document.getElementById("run-item-template"),
  artifactItemTemplate: document.getElementById("artifact-item-template"),
  taskPrompt: document.getElementById("task-prompt"),
  workspacePath: document.getElementById("workspace-path"),
  workspaceMode: document.getElementById("workspace-mode"),
  workflow: document.getElementById("workflow"),
  projectName: document.getElementById("project-name"),
  buildCommand: document.getElementById("build-command"),
  modelOverrides: document.getElementById("model-overrides"),
  permissionMode: document.getElementById("permission-mode"),
  architectureLoopPrompt: document.getElementById("architecture-loop-prompt"),
  architectureLoopMode: document.getElementById("architecture-loop-mode"),
  reviewCodingStyle: document.getElementById("review-coding-style"),
  reviewSecurity: document.getElementById("review-security"),
  reviewArchitecture: document.getElementById("review-architecture")
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

function closeEventStream(status = "disconnected") {
  if (state.eventSource) {
    state.eventSource.close();
    state.eventSource = null;
  }

  elements.eventStreamState.textContent = status;
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

function setSetupMessage(message, tone = "neutral") {
  elements.setupMessage.textContent = message;
  elements.setupMessage.dataset.tone = tone;
}

function populateSelect(select, values) {
  select.innerHTML = "";
  values.forEach(value => {
    const option = document.createElement("option");
    option.value = value;
    option.textContent = value;
    select.append(option);
  });
}

function collectRunRequest() {
  return {
    taskPrompt: elements.taskPrompt.value.trim(),
    workspacePath: elements.workspacePath.value.trim(),
    workspaceMode: elements.workspaceMode.value,
    workflow: elements.architectureLoopMode.checked ? "architecture-loop" : (elements.workflow.value.trim() || "auto"),
    projectName: elements.projectName.value.trim() || null,
    modelOverrides: parseOverrides(elements.modelOverrides.value),
    buildCommand: elements.buildCommand.value.trim() || null,
    permissionHandlerMode: elements.permissionMode.value,
    reviewLoopAgents: {
      codingStyleEnabled: elements.reviewCodingStyle.checked,
      securityEnabled: elements.reviewSecurity.checked,
      architectureEnabled: elements.reviewArchitecture.checked
    },
    architectureLoopMode: elements.architectureLoopMode.checked,
    architectureLoopPrompt: elements.architectureLoopPrompt.value.trim() || null
  };
}

function parseOverrides(raw) {
  const segments = raw.split(",").map(segment => segment.trim()).filter(Boolean);
  if (segments.length === 0) {
    return null;
  }

  const output = {};
  segments.forEach(segment => {
    const index = segment.indexOf("=");
    if (index <= 0 || index >= segment.length - 1) {
      return;
    }

    output[segment.slice(0, index).trim()] = segment.slice(index + 1).trim();
  });
  return Object.keys(output).length === 0 ? null : output;
}

function applyBootstrap(bootstrap) {
  state.bootstrap = bootstrap;
  elements.taskPrompt.value = bootstrap.defaultTaskPrompt || "";
  elements.workspacePath.value = bootstrap.workspacePath || "";
  populateSelect(elements.workspaceMode, bootstrap.workspaceModes || []);
  populateSelect(elements.permissionMode, bootstrap.permissionModes || []);
  elements.workflow.value = bootstrap.workflow || "auto";
  elements.architectureLoopMode.checked = !!bootstrap.architectureLoopMode;
  elements.architectureLoopPrompt.value = bootstrap.architectureLoopPrompt || "";
  elements.reviewCodingStyle.checked = !!bootstrap.reviewLoopAgents?.codingStyleEnabled;
  elements.reviewSecurity.checked = !!bootstrap.reviewLoopAgents?.securityEnabled;
  elements.reviewArchitecture.checked = !!bootstrap.reviewLoopAgents?.architectureEnabled;
  state.activeRun = bootstrap.activeRun;
  restoreFormState();
  renderActiveRun();
}

function renderActiveRun() {
  const activeRun = state.activeRun;
  if (!activeRun) {
    elements.activeRunStatus.textContent = "Idle";
    elements.activeRunDetail.textContent = "No run is active yet.";
    elements.cancelRun.disabled = true;
    if (!state.isUnloading) {
      closeEventStream("idle");
    }
    return;
  }

  elements.activeRunStatus.textContent = activeRun.status || "idle";
  elements.cancelRun.disabled = !activeRun.isRunning;
  const bits = [];
  if (activeRun.taskPrompt) bits.push(activeRun.taskPrompt);
  if (activeRun.runId) bits.push(`Run ${activeRun.runId}`);
  if (activeRun.failureMessage) bits.push(activeRun.failureMessage);
  elements.activeRunDetail.textContent = bits.join(" • ") || "Awaiting run details.";

  if (!activeRun.isRunning && !state.isUnloading) {
    closeEventStream("idle");
  }
}

function saveFormState() {
  const payload = {
    taskPrompt: elements.taskPrompt.value,
    workspacePath: elements.workspacePath.value,
    workspaceMode: elements.workspaceMode.value,
    workflow: elements.workflow.value,
    projectName: elements.projectName.value,
    buildCommand: elements.buildCommand.value,
    modelOverrides: elements.modelOverrides.value,
    permissionMode: elements.permissionMode.value,
    architectureLoopPrompt: elements.architectureLoopPrompt.value,
    architectureLoopMode: elements.architectureLoopMode.checked,
    reviewCodingStyle: elements.reviewCodingStyle.checked,
    reviewSecurity: elements.reviewSecurity.checked,
    reviewArchitecture: elements.reviewArchitecture.checked
  };
  window.localStorage.setItem(STORAGE_KEY, JSON.stringify(payload));
}

function restoreFormState() {
  const raw = window.localStorage.getItem(STORAGE_KEY);
  if (!raw) {
    return;
  }

  try {
    const saved = JSON.parse(raw);
    elements.taskPrompt.value = saved.taskPrompt || elements.taskPrompt.value;
    elements.workspacePath.value = saved.workspacePath || elements.workspacePath.value;
    setSelectValue(elements.workspaceMode, saved.workspaceMode);
    elements.workflow.value = saved.workflow || elements.workflow.value;
    elements.projectName.value = saved.projectName || "";
    elements.buildCommand.value = saved.buildCommand || "";
    elements.modelOverrides.value = saved.modelOverrides || "";
    setSelectValue(elements.permissionMode, saved.permissionMode);
    elements.architectureLoopPrompt.value = saved.architectureLoopPrompt || "";
    elements.architectureLoopMode.checked = !!saved.architectureLoopMode;
    elements.reviewCodingStyle.checked = saved.reviewCodingStyle ?? elements.reviewCodingStyle.checked;
    elements.reviewSecurity.checked = saved.reviewSecurity ?? elements.reviewSecurity.checked;
    elements.reviewArchitecture.checked = saved.reviewArchitecture ?? elements.reviewArchitecture.checked;
  } catch {
    window.localStorage.removeItem(STORAGE_KEY);
  }
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

function formatRunSubtitle(runId) {
  const stem = runId.slice(0, 8);
  const year = runId.slice(0, 4);
  const month = runId.slice(4, 6);
  const day = runId.slice(6, 8);
  const hour = runId.slice(9, 11);
  const minute = runId.slice(11, 13);
  return `${stem} • ${year}-${month}-${day} ${hour}:${minute}`;
}

function renderRuns() {
  elements.recentRuns.innerHTML = "";
  if (state.recentRuns.length === 0) {
    elements.recentRuns.className = "run-list empty-state";
    elements.recentRuns.textContent = "No runs found for this workspace.";
    return;
  }

  elements.recentRuns.className = "run-list";
  state.recentRuns.forEach(run => {
    const fragment = elements.runItemTemplate.content.cloneNode(true);
    const button = fragment.querySelector(".run-item");
    fragment.querySelector(".run-item-title").textContent = `Run ${run.runId}`;
    fragment.querySelector(".run-item-subtitle").textContent = formatRunSubtitle(run.runId);
    fragment.querySelector(".run-item-path").textContent = run.runDirectory;
    if (run.runId === state.selectedRunId) {
      button.classList.add("active");
    }

    button.addEventListener("click", () => selectRun(run));
    elements.recentRuns.append(button);
  });
}

function renderArtifacts() {
  elements.artifactList.innerHTML = "";
  if (state.artifacts.length === 0) {
    elements.artifactList.className = "artifact-list empty-state";
    elements.artifactList.textContent = "No artifacts found for the selected run.";
    elements.artifactPreview.textContent = "Artifact previews appear here.";
    return;
  }

  elements.artifactList.className = "artifact-list";
  state.artifacts.forEach(artifact => {
    const fragment = elements.artifactItemTemplate.content.cloneNode(true);
    const button = fragment.querySelector(".artifact-item");
    fragment.querySelector(".artifact-item-title").textContent = artifact.name;
    fragment.querySelector(".artifact-item-kind").textContent = artifact.kind;
    fragment.querySelector(".artifact-item-description").textContent = artifact.description;
    if (artifact.fullPath === state.selectedArtifactPath) {
      button.classList.add("active");
    }

    button.addEventListener("click", () => {
      state.selectedArtifactPath = artifact.fullPath;
      elements.artifactPreview.textContent = artifact.preview;
      renderArtifacts();
    });
    elements.artifactList.append(button);
  });
}

function renderTimeline() {
  elements.timeline.innerHTML = "";
  if (state.timeline.length === 0) {
    elements.timeline.className = "timeline empty-state";
    elements.timeline.textContent = "No live events yet.";
    return;
  }

  elements.timeline.className = "timeline";
  state.timeline.slice(-60).forEach(entry => {
    const article = document.createElement("article");
    const kind = readEventField(entry, "kind") || "runtime-progress";
    const source = readEventField(entry, "source") || "orchestrator";
    const agentRole = readEventField(entry, "agentRole") || source;
    const message = readEventField(entry, "message") || readEventField(entry, "details") || "Event received.";
    const timestampValue = readEventField(entry, "timestampUtc");
    const timestamp = timestampValue
      ? new Date(timestampValue).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" })
      : "Pending";
    article.className = "timeline-entry";
    article.dataset.kind = kind;
    article.innerHTML = `
      <div class="timeline-meta">
        <span>${timestamp}</span>
        <span>${escapeHtml(kind)}</span>
        <span>${escapeHtml(source)}</span>
      </div>
      <strong>${escapeHtml(agentRole)}</strong>
      <div>${escapeHtml(message)}</div>
    `;
    elements.timeline.append(article);
  });
}

function readEventField(entry, field) {
  if (!entry) {
    return null;
  }

  const pascalCase = field.charAt(0).toUpperCase() + field.slice(1);
  return entry[field] ?? entry[pascalCase] ?? null;
}

function renderInteraction() {
  const pending = state.pendingInteraction;
  if (!pending) {
    elements.interactionCard.className = "empty-state";
    elements.interactionCard.textContent = "No pending user input or permission prompts.";
    return;
  }

  const wrapper = document.createElement("div");
  wrapper.className = "interaction-surface";
  const title = document.createElement("strong");
  title.textContent = pending.kind === "permission" ? "Permission approval required" : "User input required";
  const question = document.createElement("pre");
  question.textContent = pending.question;
  wrapper.append(title, question);

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
    wrapper.append(row);
  }

  const input = document.createElement("textarea");
  input.rows = 4;
  input.placeholder = pending.kind === "permission" ? "Optional note while deciding" : "Type your response";
  wrapper.append(input);

  const actions = document.createElement("div");
  actions.className = "interaction-actions";
  if (pending.kind === "permission") {
    actions.append(
      interactionAction("Approve", "primary", () => submitPermission(true)),
      interactionAction("Deny", "danger", () => submitPermission(false))
    );
  } else {
    actions.append(interactionAction("Submit", "primary", () => submitUserInput(input.value)));
  }
  wrapper.append(actions);

  elements.interactionCard.className = "";
  elements.interactionCard.innerHTML = "";
  elements.interactionCard.append(wrapper);
}

function interactionAction(label, tone, onClick) {
  const button = document.createElement("button");
  button.type = "button";
  button.className = `interaction-action ${tone}`;
  button.textContent = label;
  button.addEventListener("click", onClick);
  return button;
}

function escapeHtml(text) {
  return String(text ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;");
}

async function loadPreflight() {
  try {
    const result = await requestJson("/api/preflight");
    elements.preflightTitle.textContent = result.isSuccess ? "Ready for runs" : "Preflight requires attention";
    elements.preflightDetail.textContent = result.isSuccess
      ? result.summary
      : (result.fixSteps?.join(" • ") || result.summary);
  } catch (error) {
    elements.preflightTitle.textContent = "Preflight failed";
    elements.preflightDetail.textContent = error.message;
  }
}

async function loadBootstrap() {
  const bootstrap = await requestJson("/api/bootstrap");
  applyBootstrap(bootstrap);
}

async function loadRuns() {
  const workspacePath = elements.workspacePath.value.trim();
  if (!workspacePath) {
    state.recentRuns = [];
    renderRuns();
    return;
  }

  elements.historyHint.textContent = `Loading runs from ${workspacePath}`;
  state.recentRuns = await requestJson(`/api/runs?workspacePath=${encodeURIComponent(workspacePath)}`) || [];
  elements.historyHint.textContent = `Loaded ${state.recentRuns.length} runs from ${workspacePath}`;
  renderRuns();
}

async function selectRun(run) {
  state.selectedRunId = run.runId;
  elements.artifactContext.textContent = `Run ${run.runId}`;
  state.artifacts = await requestJson(`/api/runs/${encodeURIComponent(run.runId)}/artifacts?workspacePath=${encodeURIComponent(elements.workspacePath.value.trim())}`) || [];
  state.selectedArtifactPath = state.artifacts[0]?.fullPath || null;
  elements.artifactPreview.textContent = state.artifacts[0]?.preview || "Artifact previews appear here.";
  renderRuns();
  renderArtifacts();
}

async function generateSummary() {
  try {
    setSetupMessage("Generating setup summary...", "neutral");
    const response = await requestJson("/api/setup-summary", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(collectRunRequest())
    });
    elements.setupSummary.textContent = response.summary || "No summary returned.";
    setSetupMessage("Setup summary ready.", "success");
  } catch (error) {
    elements.setupSummary.textContent = error.message;
    setSetupMessage("Setup summary failed.", "danger");
  }
}

async function startRun() {
  try {
    setSetupMessage("Submitting run request...", "neutral");
    const snapshot = await requestJson("/api/runs", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(collectRunRequest())
    });
    state.activeRun = snapshot;
    state.timeline = [];
    renderActiveRun();
    renderTimeline();
    connectEventStream();
    await loadRuns();
    setSetupMessage("Run accepted by local host.", "success");
  } catch (error) {
    setSetupMessage(`Run submission failed: ${error.message}`, "danger");
  }
}

async function cancelRun() {
  try {
    setSetupMessage("Canceling active run...", "warning");
    state.activeRun = await requestJson("/api/runs/active", {
      method: "DELETE"
    });
    renderActiveRun();
    setSetupMessage("Cancellation requested.", "warning");
  } catch (error) {
    setSetupMessage(`Cancel failed: ${error.message}`, "danger");
  }
}

function connectEventStream() {
  if (state.eventSource || !state.activeRun?.isRunning) {
    if (!state.activeRun?.isRunning) {
      elements.eventStreamState.textContent = "idle";
    }
    return;
  }

  const eventSource = new EventSource("/api/runs/active/events");
  state.eventSource = eventSource;
  elements.eventStreamState.textContent = "connected";

  eventSource.onmessage = event => {
    state.timeline.push(JSON.parse(event.data));
    renderTimeline();
  };

  ["run-state", "runtime-progress", "agent-delta", "copilot-session"].forEach(kind => {
    eventSource.addEventListener(kind, event => {
      state.timeline.push(JSON.parse(event.data));
      void refreshActiveRun().then(snapshot => {
        if (!snapshot?.isRunning) {
          closeEventStream("idle");
        }
      });
      renderTimeline();
      if (kind === "run-state") {
        void loadRuns();
      }
    });
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

async function refreshActiveRun() {
  state.activeRun = await requestJson("/api/runs/active");
  renderActiveRun();
  return state.activeRun;
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
    state.pendingInteraction = await requestJson("/api/interactions/pending", {
      signal: controller.signal
    });
  } catch (error) {
    if (error?.name !== "AbortError") {
      state.pendingInteraction = null;
    }
  } finally {
    state.pendingInteractionAbortController = null;
    state.pendingInteractionInFlight = false;
    renderInteraction();
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

function handleVisibilityChange() {
  if (document.hidden) {
    clearPendingInteractionPoll();
    abortPendingInteractionPoll();
    return;
  }

  void pollPendingInteraction();
}

function attachHandlers() {
  elements.generateSummary.addEventListener("click", generateSummary);
  elements.startRun.addEventListener("click", startRun);
  elements.cancelRun.addEventListener("click", cancelRun);
  elements.refreshHistory.addEventListener("click", loadRuns);
  elements.workspacePath.addEventListener("change", loadRuns);
  document.addEventListener("visibilitychange", handleVisibilityChange);
  elements.architectureLoopMode.addEventListener("change", () => {
    if (elements.architectureLoopMode.checked) {
      elements.workflow.value = "architecture-loop";
    }

    saveFormState();
  });

  [
    elements.taskPrompt,
    elements.workspacePath,
    elements.workspaceMode,
    elements.workflow,
    elements.projectName,
    elements.buildCommand,
    elements.modelOverrides,
    elements.permissionMode,
    elements.architectureLoopPrompt,
    elements.reviewCodingStyle,
    elements.reviewSecurity,
    elements.reviewArchitecture
  ].forEach(control => {
    control.addEventListener("input", saveFormState);
    control.addEventListener("change", saveFormState);
  });
}

async function init() {
  attachHandlers();
  await Promise.all([loadBootstrap(), loadPreflight()]);
  await loadRuns();
  await refreshActiveRun();
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
  setSetupMessage(`Initialization failed: ${error.message}`, "danger");
});