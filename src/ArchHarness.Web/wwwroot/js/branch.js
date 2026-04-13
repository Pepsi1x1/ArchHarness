import { state, elements, getActiveProject, toProjectBranchInfo, applyProjectBranchInfo } from './state.js';
import { requestJson } from './api.js';
import { closeComposerDropdowns } from './composer.js';
import { renderTopbar, loadProjects } from './projects.js';
import { openGitChangeReview } from './git-changes.js';

export function renderWorkspaceBranch(activeProject) {
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

export function closeWorkspaceBranchMenu() {
  if (!state.branchMenuOpen) {
    return;
  }

  state.branchMenuOpen = false;
  elements.workspaceBranchButton.setAttribute("aria-expanded", "false");
  elements.workspaceBranchMenu.classList.add("hidden");
  elements.workspaceBranchWrap.classList.remove("open");
}

export function toggleWorkspaceBranchMenu() {
  if (elements.workspaceBranchButton.disabled) {
    return;
  }

  closeComposerDropdowns();
  state.branchMenuOpen = !state.branchMenuOpen;
  renderTopbar();
}

export async function handleWorkspaceBranchSelection(projectId, branchName, options = {}) {
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

export async function ensureActiveProjectBranchInfo() {
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
