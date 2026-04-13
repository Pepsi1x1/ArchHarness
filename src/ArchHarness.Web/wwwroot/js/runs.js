import { RUN_STATUSES, STREAM_CONNECTION_STATES, DEFAULT_STREAM_EMPTY_MESSAGE } from './constants.js';
import { state, elements, getActiveProject, getSelectedRun, isSelectedRunLive, getSelectedProjectAndRun } from './state.js';
import { requestJson } from './api.js';
import { formatRunTimestamp, setSelectValue } from './utils.js';
import { renderComposerState, collectRunRequest, canPauseActiveRun } from './composer.js';
import { resetStream, showStreamStarting, closeEventStream, connectEventStream, syncSubmittedPromptSection, applyPersistedRunEvents, scrollStreamToBottom } from './stream.js';
import { renderTopbar, loadProjects } from './projects.js';
import { syncKeepAwake } from './desktop-bridge.js';
import { saveShellState } from './shell-persistence.js';
import { openModal } from './modals.js';

export function renderActiveRun() {
  const activeRun = state.activeRun;
  if (!activeRun) {
    elements.pauseRun.disabled = true;
    elements.pauseRun.textContent = "Pause";
    elements.cancelRun.disabled = true;
    if (!state.isUnloading) {
      closeEventStream(STREAM_CONNECTION_STATES.IDLE);
    }
    syncKeepAwake(false);
    renderTopbar();
    return;
  }
  elements.pauseRun.disabled = !canPauseActiveRun(activeRun);
  elements.pauseRun.textContent = activeRun.status === RUN_STATUSES.PAUSING ? "Pausing..." : "Pause";
  elements.cancelRun.disabled = !activeRun.isRunning;

  if (activeRun.runId && !state.activeRunId) {
    state.activeRunId = activeRun.runId;
  }

  if (activeRun.isRunning && isSelectedRunLive() && !state.streamOrder.length) {
    syncSubmittedPromptSection(activeRun.taskPrompt);
  }

  if (!activeRun.isRunning && isSelectedRunLive() && !state.isUnloading) {
    closeEventStream(STREAM_CONNECTION_STATES.IDLE);
  }

  syncKeepAwake(!!activeRun.isRunning);
  renderTopbar();
}

export async function loadSelectedRunStream() {
  const project = getActiveProject();
  const run = getSelectedRun(project);
  const token = ++state.selectedRunLoadToken;

  if (!project || !run) {
    state.selectedRunState = null;
    renderComposerState();
    closeEventStream(state.activeRun?.isRunning ? STREAM_CONNECTION_STATES.RECONNECTING : STREAM_CONNECTION_STATES.IDLE);
    elements.streamEmpty.textContent = DEFAULT_STREAM_EMPTY_MESSAGE;
    resetStream();
    return;
  }

  await loadSelectedRunState(project, run);
  if (token !== state.selectedRunLoadToken) {
    return;
  }

  const isLiveRun = isSelectedRunLive();

  closeEventStream(STREAM_CONNECTION_STATES.RECONNECTING);
  resetStream();
  showStreamStarting();

  try {
    const events = await requestJson(`/api/runs/${encodeURIComponent(run.runId)}/events?workspacePath=${encodeURIComponent(project.workspacePath)}`) || [];
    if (token !== state.selectedRunLoadToken) {
      return;
    }

    applyPersistedRunEvents(events, { isLive: isLiveRun });
  } catch (error) {
    if (token !== state.selectedRunLoadToken) {
      return;
    }

    if (!isLiveRun) {
      resetStream();
      elements.streamEmpty.classList.remove("hidden");
      elements.streamEmpty.textContent = error?.message || "Failed to load persisted run events.";
      return;
    }
  }

  if (!isLiveRun || token !== state.selectedRunLoadToken) {
    return;
  }

  syncSubmittedPromptSection(state.activeRun?.taskPrompt);
  if (state.streamOrder.length === 0) {
    showStreamStarting();
  }

  connectEventStream();
}

export async function startRun() {
  const request = collectRunRequest();
  await submitRunRequest(request);
}

export async function submitRunRequest(request) {
  resetStream();
  showStreamStarting();
  const snapshot = await requestJson("/api/runs", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request)
  });

  state.activeRun = snapshot;
  state.activeRunId = snapshot?.runId || null;
  elements.taskPrompt.value = "";
  saveShellState();
  renderActiveRun();
  connectEventStream();
  await loadProjects();
  await syncSelectedRunStateToCurrentSelection();
}

export async function cancelRun() {
  state.activeRun = await requestJson("/api/runs/active", {
    method: "DELETE"
  });
  renderActiveRun();
  renderRunDetailsActions();
}

export async function pauseRun() {
  if (!canPauseActiveRun(state.activeRun)) {
    return;
  }

  elements.pauseRun.disabled = true;
  elements.pauseRun.textContent = "Pausing...";

  try {
    state.activeRun = await requestJson("/api/runs/active/pause", {
      method: "POST"
    });
    if (state.activeRun?.runId) {
      state.activeRunId = state.activeRun.runId;
    }

    saveShellState();
    renderActiveRun();
    renderRunDetailsActions();
    await loadProjects();
    await loadSelectedRunStream();
  } catch (error) {
    renderActiveRun();
    renderRunDetailsActions();
    throw error;
  }
}

export async function refreshActiveRun() {
  state.activeRun = await requestJson("/api/runs/active");
  if (state.activeRun?.runId && !state.activeRunId) {
    state.activeRunId = state.activeRun.runId;
  }
  renderActiveRun();
  renderRunDetailsActions();
  return state.activeRun;
}

export async function syncSelectedRunStateToCurrentSelection() {
  const { project, run } = getSelectedProjectAndRun();
  await loadSelectedRunState(project, run);
}

export function renderRunDetailsActions() {
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

export async function resumeSelectedRun() {
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
    await syncSelectedRunStateToCurrentSelection();
  } catch (error) {
    console.error("Resume failed:", error);
    renderComposerState();
  }
}

export async function startImplementationFromPlanningRun() {
  const { project, run } = getSelectedProjectAndRun();
  if (!project || !run) {
    return;
  }

  elements.implementRun.disabled = true;
  elements.implementRun.textContent = "Starting...";

  try {
    state.activeRun = await requestJson(`/api/runs/${encodeURIComponent(run.runId)}/handoff?workspacePath=${encodeURIComponent(project.workspacePath)}`, {
      method: "POST"
    });
    state.activeRunId = state.activeRun?.runId || null;
    setSelectValue(elements.runMode, "standard");
    saveShellState();
    renderActiveRun();
    connectEventStream();
    await loadProjects();
    await syncSelectedRunStateToCurrentSelection();
  } catch (error) {
    console.error("Implementation handoff failed:", error);
    renderComposerState();
  }
}

export async function openRunDetails(project, run) {
  if (project.projectId !== state.activeProjectId) {
    import('./branch.js').then(m => m.closeWorkspaceBranchMenu());
    import('./composer.js').then(m => m.closeComposerDropdowns());
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

export function renderArtifacts() {
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
