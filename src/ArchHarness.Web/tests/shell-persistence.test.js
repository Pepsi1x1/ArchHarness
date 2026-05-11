import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MAIN_PANEL_VIEWS, STORAGE_KEY } from '../wwwroot/js/constants.js';

vi.mock('../wwwroot/js/composer.js', () => ({
  getSelectedReviewLoopAgents: () => ({ codingStyleEnabled: true, securityEnabled: false, architectureEnabled: true }),
  normalizeReviewLoopAgents: selection => ({
    codingStyleEnabled: !!selection?.codingStyleEnabled,
    securityEnabled: !!selection?.securityEnabled,
    architectureEnabled: !!selection?.architectureEnabled
  })
}));

function installShellDom() {
  document.body.innerHTML = `
    <textarea id="task-prompt"></textarea>
    <select id="run-mode"><option value="standard">Standard</option><option value="planning">Planning</option></select>
    <select id="new-project-permission"><option value="ask">Ask</option></select>
    <select id="permission-mode"><option value="ask">Ask</option><option value="auto">Auto</option></select>
    <select id="architecture-review-preset"><option value="full-review">Full Review</option><option value="focused">Focused</option></select>
  `;
}

async function loadShellModules() {
  vi.resetModules();
  installShellDom();
  const stateModule = await import('../wwwroot/js/state.js');
  const shellModule = await import('../wwwroot/js/shell-persistence.js');
  return { ...stateModule, ...shellModule };
}

function resetShellState(state) {
  state.activeProjectId = null;
  state.activeRunId = null;
  state.mainPanelView = MAIN_PANEL_VIEWS.STREAM;
  state.seenRunIds = new Set();
  state.selectedReviewLoopAgents = null;
  localStorage.clear();
}

beforeEach(() => {
  localStorage.clear();
});

describe('shell persistence', () => {
  it('saves the current shell selection and composer state', async () => {
    const { state, elements, saveShellState } = await loadShellModules();
    resetShellState(state);
    state.activeProjectId = 'project-1';
    state.activeRunId = 'run-1';
    state.mainPanelView = MAIN_PANEL_VIEWS.BRANCH_CHANGES;
    state.seenRunIds = new Set(['run-1']);
    elements.taskPrompt.value = 'Plan the work';
    elements.runMode.value = 'planning';
    elements.permissionMode.value = 'auto';
    elements.architectureReviewPreset.value = 'focused';

    saveShellState();

    expect(JSON.parse(localStorage.getItem(STORAGE_KEY))).toMatchObject({
      activeProjectId: 'project-1',
      activeRunId: 'run-1',
      mainPanelView: MAIN_PANEL_VIEWS.BRANCH_CHANGES,
      taskPrompt: 'Plan the work',
      runMode: 'planning',
      permissionMode: 'auto',
      architectureReviewPreset: 'focused',
      seenRunIds: ['run-1']
    });
  });

  it('restores known values and ignores unsupported select values', async () => {
    const { state, elements, restoreShellState } = await loadShellModules();
    resetShellState(state);
    localStorage.setItem(STORAGE_KEY, JSON.stringify({
      activeProjectId: 'project-2',
      activeRunId: 'run-2',
      mainPanelView: MAIN_PANEL_VIEWS.BRANCH_CHANGES,
      taskPrompt: 'Resume',
      runMode: 'planning',
      permissionMode: 'unsupported',
      architectureReviewPreset: 'focused',
      reviewLoopAgents: { codingStyleEnabled: false, securityEnabled: true, architectureEnabled: false },
      seenRunIds: ['run-2']
    }));

    restoreShellState();

    expect(state.activeProjectId).toBe('project-2');
    expect(state.activeRunId).toBe('run-2');
    expect(state.mainPanelView).toBe(MAIN_PANEL_VIEWS.BRANCH_CHANGES);
    expect(elements.taskPrompt.value).toBe('Resume');
    expect(elements.runMode.value).toBe('planning');
    expect(elements.permissionMode.value).toBe('ask');
    expect(elements.architectureReviewPreset.value).toBe('focused');
    expect([...state.seenRunIds]).toEqual(['run-2']);
    expect(state.selectedReviewLoopAgents).toEqual({ codingStyleEnabled: false, securityEnabled: true, architectureEnabled: false });
  });

  it('clears corrupt saved state instead of throwing', async () => {
    const { restoreShellState } = await loadShellModules();
    localStorage.setItem(STORAGE_KEY, '{nope');

    expect(() => restoreShellState()).not.toThrow();
    expect(localStorage.getItem(STORAGE_KEY)).toBeNull();
  });
});