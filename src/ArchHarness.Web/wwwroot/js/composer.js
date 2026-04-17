import { REVIEW_LOOP_DEFAULT_SELECTION, REVIEW_LOOP_AGENT_OPTIONS, WORKFLOWS, LEGACY_AUTOFILL_PROMPTS } from './constants.js';
import { state, elements, getActiveProject, getSelectedRun, isSelectedRunLive } from './state.js';
import { setSelectValue, getSelectDisplayLabel } from './utils.js';
import { saveShellState } from './shell-persistence.js';
import { closeWorkspaceBranchMenu } from './branch.js';
import { buildDropdownMenuItems } from './dropdown.js';
import { collectSubmissionAttachments } from './attachments.js';

export function normalizeReviewLoopAgents(selection) {
  const normalized = {
    codingStyleEnabled: !!selection?.codingStyleEnabled,
    securityEnabled: !!selection?.securityEnabled,
    architectureEnabled: !!selection?.architectureEnabled
  };

  if (!normalized.codingStyleEnabled && !normalized.securityEnabled && !normalized.architectureEnabled) {
    return { ...REVIEW_LOOP_DEFAULT_SELECTION };
  }

  return normalized;
}

export function getSelectedReviewLoopAgents() {
  if (!state.selectedReviewLoopAgents) {
    state.selectedReviewLoopAgents = normalizeReviewLoopAgents(state.bootstrap?.reviewLoopAgents);
  }

  return state.selectedReviewLoopAgents;
}

function summarizeReviewLoopAgents(selection) {
  const selectedLabels = REVIEW_LOOP_AGENT_OPTIONS
    .filter(option => selection[option.key])
    .map(option => option.label);

  if (selectedLabels.length === REVIEW_LOOP_AGENT_OPTIONS.length) {
    return "All Review Agents";
  }

  if (selectedLabels.length === 0) {
    return "No Review Agents";
  }

  return selectedLabels.join(", ");
}

export function toggleReviewLoopAgentSelection(agentKey) {
  const currentSelection = getSelectedReviewLoopAgents();
  const selectedCount = REVIEW_LOOP_AGENT_OPTIONS.filter(option => currentSelection[option.key]).length;
  const shouldEnable = !currentSelection[agentKey];

  if (!shouldEnable && selectedCount <= 1) {
    return;
  }

  state.selectedReviewLoopAgents = {
    ...currentSelection,
    [agentKey]: shouldEnable
  };
  saveShellState();
  renderComposerState();
}

export function clearLegacyAutofillPrompt() {
  if (LEGACY_AUTOFILL_PROMPTS.has(elements.taskPrompt.value.trim())) {
    elements.taskPrompt.value = "";
  }
}

export function syncComposerFromProject(project) {
  if (!project) {
    return;
  }

  setSelectValue(elements.permissionMode, project.permissionHandlerMode);
  setSelectValue(elements.runMode, project.architectureReviewMode ? "architecture-review" : "standard");
}

export function isArchitectureModeEnabled() {
  return elements.runMode.value === "architecture-review";
}

export function isPlanningModeEnabled() {
  return elements.runMode.value === "planning";
}

export function isWikiDocModeEnabled() {
  return elements.runMode.value === "wikidoc";
}

function getPromptPlaceholder() {
  if (isPlanningModeEnabled()) {
    return "Describe the work to plan before implementation.";
  }

  if (isWikiDocModeEnabled()) {
    return "Optional: describe the scope or focus for the wiki documentation.";
  }

  return isArchitectureModeEnabled()
    ? "Describe the architecture concern or boundary you want reviewed."
    : "Describe the change or review you want ArchHarness to run.";
}

function buildArchitecturePrompt() {
  if (elements.architectureReviewPreset.value === "full-review") {
    return "Run a full workspace architecture review.";
  }

  return null;
}

export function canPauseActiveRun(activeRun) {
  return !!activeRun?.isRunning && !!activeRun.runId && !!activeRun.runDirectory;
}

export function collectRunRequest() {
  const project = getActiveProject();
  if (!project) {
    throw new Error("Select a project before starting a run.");
  }

  const planningMode = isPlanningModeEnabled();
  const architectureLoopMode = isArchitectureModeEnabled();
  const wikiDocMode = isWikiDocModeEnabled();
  const prompt = elements.taskPrompt.value.trim();
  const reviewLoopAgents = getSelectedReviewLoopAgents();
  let architecturePrompt = null;
  let workflow = WORKFLOWS.AUTO;
  let architectureLoopPrompt = null;

  if (architectureLoopMode) {
    architecturePrompt = buildArchitecturePrompt();
    architectureLoopPrompt = architecturePrompt || project.architectureReviewPrompt || null;
  }

  if (planningMode) {
    workflow = WORKFLOWS.PLANNING;
  } else if (architectureLoopMode) {
    workflow = WORKFLOWS.ARCHITECTURE_LOOP;
  } else if (wikiDocMode) {
    workflow = WORKFLOWS.WIKIDOC;
  }

  const effectivePrompt = wikiDocMode && !prompt
    ? "Generate comprehensive wiki documentation for this workspace."
    : prompt;

  const attachments = collectSubmissionAttachments();

  return {
    taskPrompt: effectivePrompt,
    workspacePath: project.workspacePath,
    workspaceMode: project.workspaceMode,
    workflow,
    projectName: project.displayName,
    projectId: project.projectId,
    modelOverrides: null,
    buildCommand: null,
    permissionHandlerMode: wikiDocMode ? "approve-all" : (elements.permissionMode.value || project.permissionHandlerMode),
    reviewLoopAgents,
    architectureLoopMode,
    architectureLoopPrompt,
    attachments: attachments.length > 0 ? attachments : null
  };
}

export function getComposerDropdownConfigs() {
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

export function renderComposerDropdowns() {
  getComposerDropdownConfigs().forEach(renderComposerDropdown);
  renderReviewLoopAgentDropdown();
}

function renderReviewLoopAgentDropdown() {
  const selection = getSelectedReviewLoopAgents();
  const isOpen = state.composerMenuOpen === "architecture-review-agents";
  elements.architectureReviewAgentsLabel.textContent = summarizeReviewLoopAgents(selection);
  elements.architectureReviewAgentsButton.setAttribute("aria-expanded", isOpen ? "true" : "false");
  elements.architectureReviewAgentsMenu.replaceChildren();

  REVIEW_LOOP_AGENT_OPTIONS.forEach(option => {
    const item = document.createElement("button");
    item.type = "button";
    item.className = "composer-dropdown-item composer-dropdown-item-checkbox";
    item.setAttribute("role", "menuitemcheckbox");
    item.setAttribute("aria-checked", selection[option.key] ? "true" : "false");
    item.classList.toggle("current", selection[option.key]);

    const icon = document.createElement("span");
    icon.className = "composer-dropdown-check";
    icon.textContent = selection[option.key] ? "✓" : "";

    const label = document.createElement("span");
    label.className = "composer-dropdown-item-label";
    label.textContent = option.label;

    item.append(icon, label);
    item.addEventListener("click", event => {
      event.stopPropagation();
      toggleReviewLoopAgentSelection(option.key);
    });

    elements.architectureReviewAgentsMenu.append(item);
  });

  elements.architectureReviewAgentsMenu.classList.toggle("hidden", !isOpen);
  elements.architectureReviewAgentsWrap.classList.toggle("open", isOpen);
}

function renderComposerDropdown(config) {
  const isOpen = state.composerMenuOpen === config.id;
  const options = Array.from(config.select.options).map(o => ({ value: o.value, label: o.textContent || o.value }));
  config.label.textContent = getSelectDisplayLabel(config.select);
  config.button.setAttribute("aria-expanded", isOpen ? "true" : "false");

  buildDropdownMenuItems(config.menu, options, config.select.value, value => {
    selectComposerDropdownValue(config.id, value);
  });

  config.menu.classList.toggle("hidden", !isOpen || options.length === 0);
  config.wrap.classList.toggle("open", isOpen && options.length > 0);
  config.button.disabled = options.length === 0;
}

export function closeComposerDropdowns() {
  if (!state.composerMenuOpen) {
    return;
  }

  state.composerMenuOpen = null;
  renderComposerDropdowns();
}

export function toggleComposerDropdown(dropdownId) {
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

export function renderComposerState() {
  const activeProject = getActiveProject();
  const architectureMode = isArchitectureModeEnabled();
  const wikiDocMode = isWikiDocModeEnabled();
  const selectedRun = getSelectedRun(activeProject);
  const showResumeButton = !!activeProject
    && !!selectedRun
    && !state.activeRun?.isRunning
    && !!state.selectedRunState?.canResume;
  const showImplementButton = !!activeProject
    && !!selectedRun
    && !state.activeRun?.isRunning
    && !!state.selectedRunState?.canHandoff;
  elements.architectureReviewChip.classList.toggle("hidden", !architectureMode);
  elements.taskPrompt.placeholder = getPromptPlaceholder();
  elements.startRun.disabled = !activeProject || (!wikiDocMode && !elements.taskPrompt.value.trim());
  elements.startRun.textContent = isPlanningModeEnabled() ? "Plan" : wikiDocMode ? "Generate" : "Send";
  elements.resumeRun.classList.toggle("hidden", !showResumeButton);
  elements.resumeRun.disabled = !showResumeButton;
  elements.resumeRun.textContent = "Resume";
  elements.implementRun.classList.toggle("hidden", !showImplementButton);
  elements.implementRun.disabled = !showImplementButton;
  elements.implementRun.textContent = "Start Implementation";
  renderComposerDropdowns();
}
