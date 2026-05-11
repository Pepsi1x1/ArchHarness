import { beforeEach, describe, expect, it, vi } from 'vitest';

const requestJsonMock = vi.fn();
const closeComposerDropdownsMock = vi.fn();
const renderTopbarMock = vi.fn();
const loadProjectsMock = vi.fn();
const openGitChangeReviewMock = vi.fn();

vi.mock('../wwwroot/js/api.js', () => ({ requestJson: requestJsonMock }));
vi.mock('../wwwroot/js/composer.js', () => ({ closeComposerDropdowns: closeComposerDropdownsMock }));
vi.mock('../wwwroot/js/projects.js', () => ({ renderTopbar: renderTopbarMock, loadProjects: loadProjectsMock }));
vi.mock('../wwwroot/js/git-changes.js', () => ({ openGitChangeReview: openGitChangeReviewMock }));

function installBranchDom() {
  document.body.innerHTML = `
    <div id="workspace-branch-wrap" class="hidden">
      <button id="workspace-branch-button"></button>
      <span id="workspace-branch-label"></span>
      <div id="workspace-branch-menu" class="hidden"></div>
    </div>
  `;
}

async function loadBranchModules() {
  vi.resetModules();
  installBranchDom();
  const stateModule = await import('../wwwroot/js/state.js');
  const branchModule = await import('../wwwroot/js/branch.js');
  return { ...stateModule, ...branchModule };
}

beforeEach(() => {
  requestJsonMock.mockReset();
  closeComposerDropdownsMock.mockReset();
  renderTopbarMock.mockReset();
  loadProjectsMock.mockReset();
  openGitChangeReviewMock.mockReset();
});

describe('workspace branch selector', () => {
  it('renders no-project and git branch states', async () => {
    const { state, elements, renderWorkspaceBranch } = await loadBranchModules();

    renderWorkspaceBranch(null);
    expect(elements.workspaceBranchWrap.classList.contains('hidden')).toBe(true);
    expect(elements.workspaceBranchLabel.textContent).toBe('No branch');

    state.projectBranchInfoById['project-1'] = { isGitRepository: true, currentBranch: 'main', branches: ['main', 'feature'] };
    renderWorkspaceBranch({ projectId: 'project-1' });
    expect(elements.workspaceBranchWrap.classList.contains('hidden')).toBe(false);
    expect(elements.workspaceBranchLabel.textContent).toBe('main');
    expect(elements.workspaceBranchButton.disabled).toBe(false);
    expect([...elements.workspaceBranchMenu.querySelectorAll('button')].map(button => button.textContent)).toEqual(['main', 'feature']);
  });

  it('toggles the branch menu and closes composer dropdowns', async () => {
    const { state, elements, renderWorkspaceBranch, toggleWorkspaceBranchMenu, closeWorkspaceBranchMenu } = await loadBranchModules();
    state.projectBranchInfoById['project-1'] = { isGitRepository: true, currentBranch: 'main', branches: ['main', 'feature'] };
    renderWorkspaceBranch({ projectId: 'project-1' });

    toggleWorkspaceBranchMenu();
    expect(closeComposerDropdownsMock).toHaveBeenCalledTimes(1);
    expect(renderTopbarMock).toHaveBeenCalledTimes(1);
    expect(state.branchMenuOpen).toBe(true);

    closeWorkspaceBranchMenu();
    expect(state.branchMenuOpen).toBe(false);
    expect(elements.workspaceBranchMenu.classList.contains('hidden')).toBe(true);
  });

  it('switches branches and refreshes projects on success', async () => {
    const { state, handleWorkspaceBranchSelection } = await loadBranchModules();
    const onSucceeded = vi.fn();
    state.projectBranchInfoById['project-1'] = { isGitRepository: true, currentBranch: 'main', branches: ['main', 'feature'] };
    requestJsonMock.mockResolvedValueOnce({ isGitRepository: true, currentBranch: 'feature', branches: ['main', 'feature'] });

    await expect(handleWorkspaceBranchSelection('project-1', 'feature', { onSucceeded })).resolves.toBe(true);

    expect(requestJsonMock).toHaveBeenCalledWith('/api/projects/project-1/branch', expect.objectContaining({ method: 'POST', body: JSON.stringify({ branchName: 'feature' }) }));
    expect(state.projectBranchInfoById['project-1'].currentBranch).toBe('feature');
    expect(loadProjectsMock).toHaveBeenCalledTimes(1);
    expect(onSucceeded).toHaveBeenCalledTimes(1);
  });

  it('opens the git change review when a dirty worktree blocks branch switching', async () => {
    const { state, handleWorkspaceBranchSelection } = await loadBranchModules();
    const onBlocked = vi.fn();
    const onSucceeded = vi.fn();
    const onReviewClosed = vi.fn();
    state.projectBranchInfoById['project-1'] = { isGitRepository: true, currentBranch: 'main', branches: ['main', 'feature'] };
    const error = new Error('Local changes block checkout');
    error.status = 409;
    error.data = { failureCode: 'dirty-worktree', branchInfo: { isGitRepository: true, currentBranch: 'main', branches: ['main', 'feature'] } };
    requestJsonMock.mockRejectedValueOnce(error);

    await expect(handleWorkspaceBranchSelection('project-1', 'feature', { onBlocked, onSucceeded, onReviewClosed })).resolves.toBe(false);

    expect(onBlocked).toHaveBeenCalledTimes(1);
    expect(openGitChangeReviewMock).toHaveBeenCalledWith('project-1', 'feature', expect.objectContaining({ currentBranch: 'main' }), { onCompleted: onSucceeded, onClosed: onReviewClosed });
  });
});