import { beforeEach, describe, expect, it, vi } from 'vitest';
import { WORKFLOWS } from '../wwwroot/js/constants.js';

const saveShellStateMock = vi.fn();
const closeWorkspaceBranchMenuMock = vi.fn();
const collectSubmissionAttachmentsMock = vi.fn();

vi.mock('../wwwroot/js/shell-persistence.js', () => ({
  saveShellState: saveShellStateMock
}));

vi.mock('../wwwroot/js/branch.js', () => ({
  closeWorkspaceBranchMenu: closeWorkspaceBranchMenuMock
}));

vi.mock('../wwwroot/js/attachments.js', () => ({
  collectSubmissionAttachments: collectSubmissionAttachmentsMock
}));

function installComposerDom() {
  document.body.innerHTML = `
    <textarea id="task-prompt"></textarea>
    <select id="run-mode">
      <option value="standard">Standard</option>
      <option value="planning">Planning</option>
      <option value="architecture-review">Architecture Review</option>
      <option value="wikidoc">Wiki Doc</option>
    </select>
    <select id="permission-mode"><option value="ask">Ask</option><option value="approve-all">Approve All</option></select>
    <div id="run-mode-wrap"></div><button id="run-mode-button"></button><span id="run-mode-label"></span><div id="run-mode-menu"></div>
    <div id="permission-mode-wrap"></div><button id="permission-mode-button"></button><span id="permission-mode-label"></span><div id="permission-mode-menu"></div>
    <div id="architecture-review-chip" class="hidden"></div>
    <button id="architecture-review-preset-button"></button><span id="architecture-review-preset-label"></span><div id="architecture-review-preset-menu"></div>
    <select id="architecture-review-preset"><option value="full-review">Full Review</option><option value="focused">Focused</option></select>
    <div id="architecture-review-agents-wrap"></div><button id="architecture-review-agents-button"></button><span id="architecture-review-agents-label"></span><div id="architecture-review-agents-menu"></div>
    <button id="start-run"></button><button id="resume-run-button" class="hidden"></button><button id="implement-run-button" class="hidden"></button>
  `;
}

async function loadComposerModules() {
  vi.resetModules();
  installComposerDom();
  const stateModule = await import('../wwwroot/js/state.js');
  const composerModule = await import('../wwwroot/js/composer.js');
  return { ...stateModule, ...composerModule };
}

function resetComposerState(state) {
  state.bootstrap = null;
  state.projects = [];
  state.activeProjectId = null;
  state.activeRunId = null;
  state.activeRun = null;
  state.selectedRunState = null;
  state.selectedReviewLoopAgents = null;
  state.composerMenuOpen = null;
}

beforeEach(() => {
  saveShellStateMock.mockReset();
  closeWorkspaceBranchMenuMock.mockReset();
  collectSubmissionAttachmentsMock.mockReset().mockReturnValue([]);
});

describe('composer behavior', () => {
  it('normalizes review-loop agent selections and prevents turning all agents off', async () => {
    const { state, normalizeReviewLoopAgents, getSelectedReviewLoopAgents, toggleReviewLoopAgentSelection } = await loadComposerModules();
    resetComposerState(state);
    state.bootstrap = { reviewLoopAgents: { codingStyleEnabled: true, securityEnabled: false, architectureEnabled: false } };

    expect(normalizeReviewLoopAgents({})).toEqual({ codingStyleEnabled: true, securityEnabled: true, architectureEnabled: true });
    expect(getSelectedReviewLoopAgents()).toEqual({ codingStyleEnabled: true, securityEnabled: false, architectureEnabled: false });

    toggleReviewLoopAgentSelection('codingStyleEnabled');
    expect(state.selectedReviewLoopAgents).toEqual({ codingStyleEnabled: true, securityEnabled: false, architectureEnabled: false });
    expect(saveShellStateMock).not.toHaveBeenCalled();

    toggleReviewLoopAgentSelection('securityEnabled');
    expect(state.selectedReviewLoopAgents.securityEnabled).toBe(true);
    expect(saveShellStateMock).toHaveBeenCalledTimes(1);
  });

  it('preserves explicit planning mode when syncing from a project', async () => {
    const { state, elements, syncComposerFromProject } = await loadComposerModules();
    resetComposerState(state);
    elements.runMode.value = 'planning';

    syncComposerFromProject({ permissionHandlerMode: 'approve-all', architectureReviewMode: true });

    expect(elements.permissionMode.value).toBe('approve-all');
    expect(elements.runMode.value).toBe('planning');
  });

  it('collects planning, architecture-loop, and wikidoc run requests with expected workflow semantics', async () => {
    const { state, elements, collectRunRequest } = await loadComposerModules();
    resetComposerState(state);
    state.projects = [{
      projectId: 'project-1',
      displayName: 'ArchHarness',
      workspacePath: 'C:/repo',
      workspaceMode: 'full',
      permissionHandlerMode: 'ask',
      architectureReviewPrompt: 'Review boundaries'
    }];
    state.activeProjectId = 'project-1';
    collectSubmissionAttachmentsMock.mockReturnValue([{ id: 'img-1', kind: 'image' }]);

    elements.taskPrompt.value = 'Plan this';
    elements.runMode.value = 'planning';
    expect(collectRunRequest()).toMatchObject({ workflow: WORKFLOWS.PLANNING, taskPrompt: 'Plan this', permissionHandlerMode: 'ask', attachments: [{ id: 'img-1', kind: 'image' }] });

    elements.runMode.value = 'architecture-review';
    elements.architectureReviewPreset.value = 'focused';
    expect(collectRunRequest()).toMatchObject({ workflow: WORKFLOWS.ARCHITECTURE_LOOP, architectureLoopMode: true, architectureLoopPrompt: 'Review boundaries' });

    elements.runMode.value = 'wikidoc';
    elements.taskPrompt.value = '';
    expect(collectRunRequest()).toMatchObject({ workflow: WORKFLOWS.WIKIDOC, taskPrompt: 'Generate comprehensive wiki documentation for this workspace.', permissionHandlerMode: 'approve-all' });
  });

  it('renders composer state for planning mode and handoff controls', async () => {
    const { state, elements, renderComposerState } = await loadComposerModules();
    resetComposerState(state);
    state.projects = [{ projectId: 'project-1', displayName: 'Project', runs: [{ runId: 'run-1' }] }];
    state.activeProjectId = 'project-1';
    state.activeRunId = 'run-1';
    state.selectedRunState = { canResume: true, canHandoff: true };
    elements.runMode.value = 'planning';
    elements.taskPrompt.value = 'Draft a plan';

    renderComposerState();

    expect(elements.startRun.disabled).toBe(false);
    expect(elements.startRun.textContent).toBe('Plan');
    expect(elements.taskPrompt.placeholder).toBe('Describe the work to plan before implementation.');
    expect(elements.resumeRun.classList.contains('hidden')).toBe(false);
    expect(elements.implementRun.classList.contains('hidden')).toBe(false);
  });
});