import { STORAGE_KEY, MAIN_PANEL_VIEWS } from './constants.js';
import { state, elements } from './state.js';
import { setSelectValue } from './utils.js';
import { getSelectedReviewLoopAgents, normalizeReviewLoopAgents } from './composer.js';

export function saveShellState() {
  const payload = {
    activeProjectId: state.activeProjectId,
    activeRunId: state.activeRunId,
    mainPanelView: state.mainPanelView,
    taskPrompt: elements.taskPrompt.value,
    runMode: elements.runMode.value,
    permissionMode: elements.permissionMode.value,
    architectureReviewPreset: elements.architectureReviewPreset.value,
    reviewLoopAgents: getSelectedReviewLoopAgents(),
    seenRunIds: [...state.seenRunIds]
  };
  globalThis.localStorage.setItem(STORAGE_KEY, JSON.stringify(payload));
}

export function restoreShellState() {
  const raw = globalThis.localStorage.getItem(STORAGE_KEY);
  if (!raw) {
    return;
  }

  try {
    const saved = JSON.parse(raw);
    state.activeProjectId = saved.activeProjectId || null;
    state.activeRunId = saved.activeRunId || null;
    state.mainPanelView = saved.mainPanelView === MAIN_PANEL_VIEWS.BRANCH_CHANGES
      ? MAIN_PANEL_VIEWS.BRANCH_CHANGES
      : MAIN_PANEL_VIEWS.STREAM;
    state.seenRunIds = new Set(Array.isArray(saved.seenRunIds) ? saved.seenRunIds : []);
    elements.taskPrompt.value = saved.taskPrompt || "";
    setSelectValue(elements.runMode, saved.runMode);
    setSelectValue(elements.permissionMode, saved.permissionMode);
    setSelectValue(elements.architectureReviewPreset, saved.architectureReviewPreset);
    state.selectedReviewLoopAgents = normalizeReviewLoopAgents(saved.reviewLoopAgents);
  } catch {
    globalThis.localStorage.removeItem(STORAGE_KEY);
  }
}
