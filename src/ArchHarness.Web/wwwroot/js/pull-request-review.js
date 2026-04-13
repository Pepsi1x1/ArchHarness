import { WORKFLOWS, REVIEW_PROVIDER_NAME_MAX_LENGTH, REVIEW_PULL_REQUEST_ID_MAX_LENGTH } from './constants.js';
import { state, elements, applyProjectBranchInfo } from './state.js';
import { requestJson, requestEventStream } from './api.js';
import { summarizeWorkspacePath, equalIgnoringCase, setSelectValue } from './utils.js';
import { renderComposerState, getSelectedReviewLoopAgents } from './composer.js';
import { normalizeProviderCollection, getProviderMetaByType, normalizeReviewLookupValue, normalizeReviewPullRequestId } from './providers.js';
import { openModal, closeModal, registerModalPreClose } from './modals.js';
import { handleWorkspaceBranchSelection } from './branch.js';
import { openGitChangeReview } from './git-changes.js';
import { loadProjects } from './projects.js';
import { submitRunRequest } from './runs.js';
import { desktopBridge, selectFolderWithDesktopBridge } from './desktop-bridge.js';

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
  folderPath: "",
  prFiles: [],
  prFilesError: "",
  startReviewError: "",
  isPreparingWorkspace: false,
  isStartingReview: false
};

const REVIEW_PR_STEP_TITLES = ["Select Provider", "Select Pull Request", "Working Folder", "Confirm Review"];
const REVIEW_PR_STEP_IDS = ["review-pr-step-provider", "review-pr-step-list", "review-pr-step-folder", "review-pr-step-confirm"];

registerModalPreClose("review-pr-modal", () => {
  abortPullRequestStream();
});

export async function openReviewPrModal() {
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
    startReviewError: "",
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

export async function startPullRequestReview() {
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
  reviewPrState.startReviewError = "";
  renderConfirmStep();
  updateReviewPrNavigation();

  try {
    setSelectValue(elements.runMode, "architecture-review");
    setSelectValue(elements.architectureReviewPreset, "focused-review");
    renderComposerState();

    await submitRunRequest({
      taskPrompt: "",
      workspacePath: project.workspacePath,
      workspaceMode: project.workspaceMode || "existing-git",
      workflow: WORKFLOWS.ARCHITECTURE_LOOP,
      projectName: project.displayName,
      projectId: project.projectId,
      modelOverrides: null,
      buildCommand: null,
      permissionHandlerMode: elements.permissionMode.value || project.permissionHandlerMode,
      reviewLoopAgents: getSelectedReviewLoopAgents(),
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
      reviewPrState.startReviewError = "";
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
      reviewPrState.startReviewError = "";
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
    reviewPrState.startReviewError = "";
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
    reviewPrState.startReviewError = "";
    renderConfirmStep();
    return;
  }

  try {
    const qs = params.toString();
    const files = await requestJson(`/api/providers/${encodeURIComponent(providerName)}/pullrequests/${encodeURIComponent(prId)}/files${qs ? "?" + qs : ""}`);
    reviewPrState.prFiles = Array.isArray(files) ? files : [];
    reviewPrState.prFilesError = "";
    reviewPrState.startReviewError = "";
  } catch (error) {
    reviewPrState.prFiles = [];
    reviewPrState.prFilesError = error?.message || "Failed to load changed files for this pull request.";
    reviewPrState.startReviewError = "";
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
    statusEl.classList.toggle("review-pr-error-note", Boolean(reviewPrState.startReviewError || reviewPrState.prFilesError));

    if (reviewPrState.startReviewError) {
      statusEl.textContent = reviewPrState.startReviewError;
      statusEl.classList.remove("hidden");
    } else if (reviewPrState.prFilesError) {
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

export async function handleReviewPrNext() {
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

export function handleReviewPrBack() {
  showReviewPrStep(reviewPrState.step - 1);

  if (reviewPrState.step === 2) {
    renderFolderStep();
  } else if (reviewPrState.step === 3) {
    renderConfirmStep();
  }
}

export async function pickReviewPrFolder() {
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

export { clearPullRequestFilter };
