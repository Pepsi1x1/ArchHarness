import { describe, expect, it, vi } from 'vitest';

function installModalDom() {
  document.body.innerHTML = `
    <div id="modal-backdrop" class="hidden"></div>
    <section id="settings-modal" class="hidden" aria-hidden="true"></section>
  `;
}

async function loadModalStateModules() {
  vi.resetModules();
  installModalDom();
  const stateModule = await import('../wwwroot/js/state.js');
  const modalModule = await import('../wwwroot/js/modals.js');
  return { ...stateModule, ...modalModule };
}

describe('modal and state helpers', () => {
  it('opens and closes modals with backdrop state and pre-close callbacks', async () => {
    const { state, elements, openModal, closeModal, registerModalPreClose } = await loadModalStateModules();
    const afterCloseCalls = [];
    registerModalPreClose('settings-modal', (_openModalId, options) => ({
      afterClose: () => afterCloseCalls.push(options.reason)
    }));

    openModal('settings-modal');
    expect(state.openModalId).toBe('settings-modal');
    expect(document.getElementById('settings-modal').classList.contains('hidden')).toBe(false);
    expect(elements.modalBackdrop.classList.contains('hidden')).toBe(false);

    closeModal({ reason: 'done' });
    expect(state.openModalId).toBeNull();
    expect(document.getElementById('settings-modal').getAttribute('aria-hidden')).toBe('true');
    expect(elements.modalBackdrop.classList.contains('hidden')).toBe(true);
    expect(afterCloseCalls).toEqual(['done']);
  });

  it('selects active projects, runs, and branch info safely', async () => {
    const {
      state,
      getActiveProject,
      getProjectById,
      getProjectRunCount,
      getSelectedProjectAndRun,
      getSelectedRun,
      isSelectedRunLive,
      applyProjectBranchInfo
    } = await loadModalStateModules();
    state.projects = [{ projectId: 'project-1', runs: [{ runId: 'run-1' }, { runId: 'run-2' }] }];
    state.activeProjectId = 'project-1';
    state.activeRunId = 'run-2';
    state.activeRun = { runId: 'run-2', isRunning: true };

    expect(getActiveProject()?.projectId).toBe('project-1');
    expect(getProjectById('project-1')).toBe(getActiveProject());
    expect(getSelectedRun()?.runId).toBe('run-2');
    expect(getSelectedProjectAndRun().run?.runId).toBe('run-2');
    expect(getProjectRunCount(getActiveProject())).toBe(2);
    expect(isSelectedRunLive()).toBe(true);

    applyProjectBranchInfo('project-1', { isGitRepository: true, currentBranch: 'main', branches: ['main', 'feature'] });
    expect(state.projectBranchInfoById['project-1']).toEqual({ isGitRepository: true, currentBranch: 'main', branches: ['main', 'feature'] });
  });
});