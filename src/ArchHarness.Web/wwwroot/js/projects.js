import { MAIN_PANEL_VIEWS } from './constants.js';
import { state, elements, getActiveProject } from './state.js';
import { requestJson } from './api.js';
import { summarizeWorkspacePath, populateSelect, setSelectValue, runDateFromId, timeAgo } from './utils.js';
import { normalizeReviewLoopAgents, renderComposerState, syncComposerFromProject, closeComposerDropdowns, clearLegacyAutofillPrompt } from './composer.js';
import { renderWorkspaceBranch, ensureActiveProjectBranchInfo, closeWorkspaceBranchMenu } from './branch.js';
import { renderMainPanelView, loadBranchChangesForActiveProject } from './git-changes.js';
import { saveShellState } from './shell-persistence.js';
import { openModal, closeModal } from './modals.js';
import { desktopBridge, selectFolderWithDesktopBridge } from './desktop-bridge.js';
import { renderActiveRun, loadSelectedRunStream, openRunDetails } from './runs.js';
import { populateSettingsPermissionMode } from './settings.js';

export function renderTopbar() {
  const activeProject = getActiveProject();

  elements.workspaceTitle.textContent = activeProject ? activeProject.displayName : "No project selected";
  renderWorkspaceBranch(activeProject);
  void ensureActiveProjectBranchInfo();
  renderComposerState();
  renderMainPanelView();
}

export function renderProjects() {
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

export async function loadBootstrap() {
  const bootstrap = await requestJson("/api/bootstrap");
  applyBootstrap(bootstrap);
}

export function applyBootstrap(bootstrap) {
  state.bootstrap = bootstrap;
  state.selectedReviewLoopAgents = normalizeReviewLoopAgents(
    state.selectedReviewLoopAgents || bootstrap.reviewLoopAgents
  );
  console.log("DEBUG permissionMode:", elements.permissionMode, "newProjectPermission:", elements.newProjectPermission);
  populateSelect(elements.permissionMode, bootstrap.permissionModes || []);
  populateSelect(elements.newProjectPermission, bootstrap.permissionModes || []);
  populateSettingsPermissionMode();

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

export async function loadProjects() {
  state.projects = await requestJson("/api/projects?maxRunsPerProject=24") || [];
  const knownProjectIds = new Set(state.projects.map(project => project.projectId));
  const awaitingActiveRunId = state.activeRun?.isRunning && !state.activeRun?.runId;
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

  if (!state.activeRunId && !awaitingActiveRunId && state.projects.length > 0) {
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
  if (state.mainPanelView === MAIN_PANEL_VIEWS.BRANCH_CHANGES) {
    void loadBranchChangesForActiveProject({ force: true });
  }
  saveShellState();
}

export async function createProject(event) {
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

export async function pickProjectFolder() {
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
