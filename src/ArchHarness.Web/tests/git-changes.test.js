import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MAIN_PANEL_VIEWS } from '../wwwroot/js/constants.js';

const requestJsonMock = vi.fn();
const saveShellStateMock = vi.fn();
const openModalMock = vi.fn();
const closeModalMock = vi.fn();
const registerModalPreCloseMock = vi.fn();
const handleWorkspaceBranchSelectionMock = vi.fn();

vi.mock('../wwwroot/js/api.js', () => ({ requestJson: requestJsonMock }));
vi.mock('../wwwroot/js/shell-persistence.js', () => ({ saveShellState: saveShellStateMock }));
vi.mock('../wwwroot/js/modals.js', () => ({
  openModal: openModalMock,
  closeModal: closeModalMock,
  registerModalPreClose: registerModalPreCloseMock
}));
vi.mock('../wwwroot/js/branch.js', () => ({ handleWorkspaceBranchSelection: handleWorkspaceBranchSelectionMock }));

function installGitChangesDom() {
  document.body.innerHTML = `
    <button id="stream-view-button"></button><button id="branch-changes-view-button"></button>
    <section id="stream-view"></section><section id="branch-changes-view" class="hidden"></section>
    <h2 id="branch-changes-title"></h2><p id="branch-changes-summary"></p><button id="branch-changes-refresh"></button>
    <div id="branch-change-list"></div><div id="branch-diff-meta"></div><div id="branch-diff-preview"></div>
    <h2 id="git-changes-title"></h2><p id="git-changes-summary"></p><div id="git-changes-action-status"></div>
    <button id="git-changes-stash-button"></button><button id="git-changes-close-button"></button>
    <div id="git-change-list"></div><div id="git-diff-meta"></div><div id="git-diff-preview"></div>
  `;
}

async function loadGitChangeModules() {
  vi.resetModules();
  installGitChangesDom();
  const stateModule = await import('../wwwroot/js/state.js');
  const gitChangesModule = await import('../wwwroot/js/git-changes.js');
  return { ...stateModule, ...gitChangesModule };
}

beforeEach(() => {
  requestJsonMock.mockReset();
  saveShellStateMock.mockReset();
  openModalMock.mockReset();
  closeModalMock.mockReset();
  registerModalPreCloseMock.mockReset();
  handleWorkspaceBranchSelectionMock.mockReset();
});

describe('git changes screens', () => {
  it('renders the branch changes panel empty state when no project is selected', async () => {
    const { elements, renderBranchChangesPanel } = await loadGitChangeModules();

    renderBranchChangesPanel();

    expect(elements.branchChangesTitle.textContent).toBe('Current Branch Changes');
    expect(elements.branchChangesSummary.textContent).toContain('Select a project');
    expect(elements.branchChangesRefresh.disabled).toBe(true);
    expect(elements.branchDiffPreview.textContent).toContain('Select a project');
  });

  it('switches main panel view, persists it, and triggers branch refresh', async () => {
    const { state, elements, setMainPanelView } = await loadGitChangeModules();
    state.projects = [{ projectId: 'project-1', displayName: 'Project', workspacePath: 'C:/repo' }];
    state.activeProjectId = 'project-1';
    requestJsonMock.mockResolvedValueOnce({ currentBranch: 'main', files: [] });

    setMainPanelView(MAIN_PANEL_VIEWS.BRANCH_CHANGES, { forceRefresh: true });
    await Promise.resolve();

    expect(state.mainPanelView).toBe(MAIN_PANEL_VIEWS.BRANCH_CHANGES);
    expect(saveShellStateMock).toHaveBeenCalledTimes(1);
    expect(elements.branchChangesView.hidden).toBe(false);
    expect(requestJsonMock).toHaveBeenCalledWith('/api/projects/project-1/git/changes');
  });

  it('loads changed files and selected diff for the active project', async () => {
    const { state, elements, loadBranchChangesForActiveProject } = await loadGitChangeModules();
    state.projects = [{ projectId: 'project-1', displayName: 'Project', workspacePath: 'C:/repo' }];
    state.activeProjectId = 'project-1';
    requestJsonMock
      .mockResolvedValueOnce({ currentBranch: 'feature', files: [{ path: 'src/app.js', status: 'Modified' }] })
      .mockResolvedValueOnce({ diffText: 'diff --git a/src/app.js b/src/app.js\n@@ -1 +1 @@\n-old\n+new' });

    await loadBranchChangesForActiveProject({ force: true });

    expect(state.branchChanges.currentBranch).toBe('feature');
    expect(state.branchChanges.selectedPath).toBe('src/app.js');
    expect(elements.branchChangesSummary.textContent).toContain('1 changed file');
    expect(elements.branchChangeList.textContent).toContain('src/app.js');
    expect(elements.branchDiffPreview.textContent).toContain('new');
  });

  it('opens branch switch review and stashes before continuing', async () => {
    const { state, elements, openGitChangeReview, stashGitChangesAndContinue } = await loadGitChangeModules();
    requestJsonMock
      .mockResolvedValueOnce({ currentBranch: 'feature', files: [{ path: 'src/app.js', status: 'Modified' }] })
      .mockResolvedValueOnce({ diffText: 'diff --git a/src/app.js b/src/app.js\n@@ -1 +1 @@\n-old\n+new' })
      .mockResolvedValueOnce({ branchInfo: { isGitRepository: true, currentBranch: 'feature', branches: ['feature', 'main'] }, workingTreeStatus: { currentBranch: 'feature', files: [] } });

    await openGitChangeReview('project-1', 'main', { currentBranch: 'feature' });
    expect(openModalMock).toHaveBeenCalledWith('git-changes-modal');
    expect(elements.gitChangesStashButton.textContent).toBe('Stash and switch to main');

    await stashGitChangesAndContinue();

    expect(requestJsonMock).toHaveBeenLastCalledWith('/api/projects/project-1/git/stash', expect.objectContaining({ method: 'POST' }));
    expect(state.projectBranchInfoById['project-1'].currentBranch).toBe('feature');
    expect(closeModalMock).toHaveBeenCalledWith({ skipGitChangeReviewClose: true });
    expect(handleWorkspaceBranchSelectionMock).toHaveBeenCalledWith('project-1', 'main', { onSucceeded: null });
  });
});