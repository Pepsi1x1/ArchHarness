import { MAIN_PANEL_VIEWS } from './constants.js';
import { state, elements, getActiveProject, applyProjectBranchInfo } from './state.js';
import { requestJson } from './api.js';
import { equalIgnoringCase } from './utils.js';
import { getGitChangeStatusClass, createGitDiffMessageView, setGitDiffPreviewContent, createSideBySideDiffView } from './diff-viewer.js';
import { openModal, closeModal, registerModalPreClose } from './modals.js';
import { saveShellState } from './shell-persistence.js';
import { handleWorkspaceBranchSelection } from './branch.js';

export function createEmptyGitChangeReviewState() {
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

function createEmptyBranchChangesState() {
  return {
    projectId: null,
    currentBranch: null,
    files: [],
    selectedPath: null,
    diffByPath: {},
    loading: false,
    diffLoadingPath: null,
    error: "",
    requestToken: 0
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

function applyWorkingTreeStatusToGitChangeState(gitChangeState, workingTreeStatus) {
  if (!gitChangeState || !workingTreeStatus) {
    return;
  }

  gitChangeState.currentBranch = workingTreeStatus.currentBranch || gitChangeState.currentBranch;
  gitChangeState.files = Array.isArray(workingTreeStatus.files) ? workingTreeStatus.files : [];

  const stillSelected = gitChangeState.files.some(file => file.path === gitChangeState.selectedPath);
  if (!stillSelected) {
    gitChangeState.selectedPath = gitChangeState.files[0]?.path || null;
  }

  gitChangeState.diffByPath = Object.fromEntries(
    Object.entries(gitChangeState.diffByPath).filter(([path]) => gitChangeState.files.some(file => file.path === path))
  );
}

function renderGitChangeBrowser(review, target, handlers) {
  target.changeList.replaceChildren();

  if (review.loading && review.files.length === 0) {
    target.changeList.className = "git-change-list empty-state";
    target.changeList.textContent = "Loading changed files...";
    target.diffMeta.textContent = "Loading Git diff...";
    setGitDiffPreviewContent(target.diffPreview, createGitDiffMessageView("Loading changed files..."));
    return;
  }

  if (review.error) {
    target.changeList.className = "git-change-list empty-state";
    target.changeList.textContent = review.error;
    target.diffMeta.textContent = "Git diff unavailable";
    setGitDiffPreviewContent(target.diffPreview, createGitDiffMessageView(review.error));
    return;
  }

  if (!Array.isArray(review.files) || review.files.length === 0) {
    target.changeList.className = "git-change-list empty-state";
    target.changeList.textContent = "No local changes were found.";
    target.diffMeta.textContent = "No diff to show";
    setGitDiffPreviewContent(target.diffPreview, createGitDiffMessageView("No local changes were found."));
    return;
  }

  target.changeList.className = "git-change-list";

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

      handlers.onSelectPath(file.path);
    });
    target.changeList.append(button);
  });

  const selectedFile = review.files.find(file => file.path === review.selectedPath) || review.files[0];
  if (!selectedFile) {
    target.diffMeta.textContent = "Select a changed file to view its diff.";
    setGitDiffPreviewContent(target.diffPreview, createGitDiffMessageView("Select a changed file to view its diff."));
    return;
  }

  review.selectedPath = selectedFile.path;
  const cachedDiff = review.diffByPath[selectedFile.path] || null;
  target.diffMeta.textContent = `${selectedFile.path} • ${selectedFile.status || "Modified"}`;
  if (review.diffLoadingPath === selectedFile.path) {
    setGitDiffPreviewContent(target.diffPreview, createGitDiffMessageView("Loading Git diff..."));
    return;
  }

  if (cachedDiff?.error) {
    setGitDiffPreviewContent(target.diffPreview, createGitDiffMessageView(cachedDiff.error));
    return;
  }

  if (cachedDiff?.diffText) {
    setGitDiffPreviewContent(target.diffPreview, createSideBySideDiffView(cachedDiff.diffText));
    return;
  }

  setGitDiffPreviewContent(target.diffPreview, createGitDiffMessageView("Select a changed file to view its diff."));
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
  renderGitChangeBrowser(review, {
    changeList: elements.gitChangeList,
    diffMeta: elements.gitDiffMeta,
    diffPreview: elements.gitDiffPreview
  }, {
    onSelectPath(path) {
      state.gitChangeReview.selectedPath = path;
      renderGitChangeReview();
      void ensureSelectedGitDiffFor(state.gitChangeReview, renderGitChangeReview);
    }
  });
}

async function ensureSelectedGitDiffFor(review, renderCallback) {
  if (!review.projectId || !review.selectedPath) {
    return;
  }

  if (review.diffByPath[review.selectedPath] || review.diffLoadingPath === review.selectedPath) {
    return;
  }

  const diffPath = review.selectedPath;
  review.diffLoadingPath = diffPath;
  renderCallback();

  try {
    const response = await requestJson(`/api/projects/${encodeURIComponent(review.projectId)}/git/diff?path=${encodeURIComponent(diffPath)}`);
    review.diffByPath[diffPath] = {
      diffText: response?.diffText || "No textual diff is available for the selected file."
    };
  } catch (error) {
    review.diffByPath[diffPath] = {
      error: error?.message || "Failed to load the selected Git diff."
    };
  } finally {
    if (review.diffLoadingPath === diffPath) {
      review.diffLoadingPath = null;
    }

    renderCallback();
  }
}

async function ensureSelectedGitDiff() {
  await ensureSelectedGitDiffFor(state.gitChangeReview, renderGitChangeReview);
}

export function renderBranchChangesPanel() {
  const activeProject = getActiveProject();
  const branchChanges = state.branchChanges;
  const activeProjectId = activeProject?.projectId || null;
  const activeProjectName = activeProject?.displayName || "Current project";
  const isActiveProjectLoaded = !!activeProjectId && branchChanges.projectId === activeProjectId;

  if (!activeProject) {
    elements.branchChangesTitle.textContent = "Current Branch Changes";
    elements.branchChangesSummary.textContent = "Select a project to inspect local branch changes.";
    elements.branchChangesRefresh.disabled = true;
    elements.branchChangeList.className = "git-change-list empty-state";
    elements.branchChangeList.textContent = "Select a project to load changed files.";
    elements.branchDiffMeta.textContent = "No project selected";
    setGitDiffPreviewContent(elements.branchDiffPreview, createGitDiffMessageView("Select a project to view current branch changes."));
    return;
  }

  elements.branchChangesTitle.textContent = branchChanges.currentBranch
    ? `Changes on ${branchChanges.currentBranch}`
    : `Current Branch Changes`;
  if (branchChanges.loading) {
    elements.branchChangesSummary.textContent = `Loading local changes for ${activeProjectName}...`;
  } else if (!isActiveProjectLoaded) {
    elements.branchChangesSummary.textContent = `Load local changes for ${activeProjectName}.`;
  } else if (branchChanges.error) {
    elements.branchChangesSummary.textContent = branchChanges.error;
  } else if (branchChanges.files.length === 0) {
    elements.branchChangesSummary.textContent = branchChanges.currentBranch
      ? `${activeProjectName} is clean on ${branchChanges.currentBranch}.`
      : `${activeProjectName} has no local branch changes.`;
  } else {
    const fileLabel = branchChanges.files.length === 1 ? "1 changed file" : `${branchChanges.files.length} changed files`;
    elements.branchChangesSummary.textContent = branchChanges.currentBranch
      ? `${fileLabel} on ${branchChanges.currentBranch} for ${activeProjectName}.`
      : `${fileLabel} for ${activeProjectName}.`;
  }

  elements.branchChangesRefresh.disabled = branchChanges.loading;
  renderGitChangeBrowser(branchChanges, {
    changeList: elements.branchChangeList,
    diffMeta: elements.branchDiffMeta,
    diffPreview: elements.branchDiffPreview
  }, {
    onSelectPath(path) {
      state.branchChanges.selectedPath = path;
      renderBranchChangesPanel();
      void ensureSelectedGitDiffFor(state.branchChanges, renderBranchChangesPanel);
    }
  });
}

export function renderMainPanelView() {
  const isStreamView = state.mainPanelView !== MAIN_PANEL_VIEWS.BRANCH_CHANGES;
  elements.streamViewButton.classList.toggle("active", isStreamView);
  elements.branchChangesViewButton.classList.toggle("active", !isStreamView);
  elements.streamViewButton.setAttribute("aria-selected", isStreamView ? "true" : "false");
  elements.branchChangesViewButton.setAttribute("aria-selected", isStreamView ? "false" : "true");
  elements.streamView.classList.toggle("hidden", !isStreamView);
  elements.streamView.hidden = !isStreamView;
  elements.branchChangesView.classList.toggle("hidden", isStreamView);
  elements.branchChangesView.hidden = isStreamView;

  if (!isStreamView) {
    renderBranchChangesPanel();
  }
}

export function setMainPanelView(view, options = {}) {
  const nextView = view === MAIN_PANEL_VIEWS.BRANCH_CHANGES
    ? MAIN_PANEL_VIEWS.BRANCH_CHANGES
    : MAIN_PANEL_VIEWS.STREAM;
  const changed = state.mainPanelView !== nextView;
  state.mainPanelView = nextView;
  if (changed || options.persist === true) {
    saveShellState();
  }

  renderMainPanelView();

  if (nextView === MAIN_PANEL_VIEWS.BRANCH_CHANGES && (changed || options.forceRefresh === true)) {
    void loadBranchChangesForActiveProject({ force: true });
  }
}

export async function loadBranchChangesForActiveProject(options = {}) {
  const activeProject = getActiveProject();
  if (!activeProject) {
    state.branchChanges = createEmptyBranchChangesState();
    renderBranchChangesPanel();
    return;
  }

  const force = options.force === true;
  const projectChanged = state.branchChanges.projectId !== activeProject.projectId;
  if (!force && !projectChanged && (state.branchChanges.loading || state.branchChanges.files.length > 0 || state.branchChanges.error)) {
    renderBranchChangesPanel();
    return;
  }

  const previousState = projectChanged ? createEmptyBranchChangesState() : state.branchChanges;
  const nextToken = previousState.requestToken + 1;
  state.branchChanges = {
    ...previousState,
    projectId: activeProject.projectId,
    currentBranch: projectChanged ? null : previousState.currentBranch,
    loading: true,
    diffLoadingPath: null,
    error: "",
    requestToken: nextToken
  };
  renderBranchChangesPanel();

  try {
    const response = await requestJson(`/api/projects/${encodeURIComponent(activeProject.projectId)}/git/changes`);
    if (state.branchChanges.projectId !== activeProject.projectId || state.branchChanges.requestToken !== nextToken) {
      return;
    }

    applyWorkingTreeStatusToGitChangeState(state.branchChanges, response);
    state.branchChanges.loading = false;
    renderBranchChangesPanel();
    await ensureSelectedGitDiffFor(state.branchChanges, renderBranchChangesPanel);
  } catch (error) {
    if (state.branchChanges.projectId !== activeProject.projectId || state.branchChanges.requestToken !== nextToken) {
      return;
    }

    state.branchChanges.loading = false;
    state.branchChanges.error = error?.message || "Failed to load local Git changes.";
    renderBranchChangesPanel();
  }
}

export async function openGitChangeReview(projectId, targetBranch, branchInfo, options = {}) {
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

export async function stashGitChangesAndContinue() {
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
    applyWorkingTreeStatusToGitChangeState(state.gitChangeReview, response?.workingTreeStatus);

    const targetBranch = review.targetBranch;
    const projectId = review.projectId;
    const onCompleted = review.onCompleted;
    closeModal({ skipGitChangeReviewClose: true });
    await handleWorkspaceBranchSelection(projectId, targetBranch, { onSucceeded: onCompleted });
  } catch (error) {
    applyProjectBranchInfo(review.projectId, error?.data?.branchInfo);
    applyWorkingTreeStatusToGitChangeState(state.gitChangeReview, error?.data?.workingTreeStatus);
    state.gitChangeReview.actionError = error?.message || "Failed to stash local changes.";
    renderGitChangeReview();
  } finally {
    if (state.openModalId === "git-changes-modal") {
      state.gitChangeReview.stashInFlight = false;
      renderGitChangeReview();
    }
  }
}

// Register cleanup handler for the git changes modal via Dependency Inversion
registerModalPreClose((modalId, options) => {
  if (modalId === "git-changes-modal") {
    const skipClose = options.skipGitChangeReviewClose === true;
    const handler = skipClose ? null : state.gitChangeReview.onClosed;
    state.gitChangeReview = createEmptyGitChangeReviewState();
    return handler ? { afterClose: handler } : null;
  }
});
