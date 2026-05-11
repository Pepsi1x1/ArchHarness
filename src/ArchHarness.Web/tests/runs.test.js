import { beforeEach, describe, expect, it, vi } from 'vitest';
import { RUN_STATUSES, WORKFLOWS } from '../wwwroot/js/constants.js';

const requestJsonMock = vi.fn();
const postPlanningFollowUpMock = vi.fn();
const renderComposerStateMock = vi.fn();
const collectRunRequestMock = vi.fn();
const canPauseActiveRunMock = vi.fn();
let wikiDocModeEnabled = false;
const resetStreamMock = vi.fn();
const showStreamStartingMock = vi.fn();
const closeEventStreamMock = vi.fn();
const connectEventStreamMock = vi.fn();
const syncSubmittedPromptSectionMock = vi.fn();
const applyPersistedRunEventsMock = vi.fn();
const renderTopbarMock = vi.fn();
const loadProjectsMock = vi.fn();
const syncKeepAwakeMock = vi.fn();
const saveShellStateMock = vi.fn();
const openModalMock = vi.fn();
const clearComposerAttachmentsMock = vi.fn();
const collectSubmissionAttachmentsMock = vi.fn();
const submitPlanApprovalMock = vi.fn();

vi.mock('../wwwroot/js/api.js', () => ({ requestJson: requestJsonMock, postPlanningFollowUp: postPlanningFollowUpMock }));
vi.mock('../wwwroot/js/composer.js', () => ({
  renderComposerState: renderComposerStateMock,
  collectRunRequest: collectRunRequestMock,
  canPauseActiveRun: canPauseActiveRunMock,
  isWikiDocModeEnabled: () => wikiDocModeEnabled
}));
vi.mock('../wwwroot/js/stream.js', () => ({
  resetStream: resetStreamMock,
  showStreamStarting: showStreamStartingMock,
  closeEventStream: closeEventStreamMock,
  connectEventStream: connectEventStreamMock,
  syncSubmittedPromptSection: syncSubmittedPromptSectionMock,
  applyPersistedRunEvents: applyPersistedRunEventsMock
}));
vi.mock('../wwwroot/js/projects.js', () => ({ renderTopbar: renderTopbarMock, loadProjects: loadProjectsMock }));
vi.mock('../wwwroot/js/desktop-bridge.js', () => ({ desktopBridge: {}, syncKeepAwake: syncKeepAwakeMock }));
vi.mock('../wwwroot/js/shell-persistence.js', () => ({ saveShellState: saveShellStateMock }));
vi.mock('../wwwroot/js/modals.js', () => ({ openModal: openModalMock }));
vi.mock('../wwwroot/js/attachments.js', () => ({ clearComposerAttachments: clearComposerAttachmentsMock, collectSubmissionAttachments: collectSubmissionAttachmentsMock }));
vi.mock('../wwwroot/js/interactions.js', () => ({ submitPlanApproval: submitPlanApprovalMock }));

function installRunsDom() {
  document.body.innerHTML = `
    <button id="pause-run"></button><button id="cancel-run"></button>
    <textarea id="task-prompt"></textarea>
    <select id="run-mode"><option value="standard">standard</option></select>
    <button id="resume-run-button" class="hidden"></button><button id="implement-run-button" class="hidden"></button>
    <button id="planning-followup-button" class="hidden"></button>
    <h2 id="run-details-title"></h2><div id="artifact-summary"></div><pre id="artifact-preview"></pre><div id="artifact-list"></div>
    <template id="artifact-template">
      <button class="artifact-item"><strong class="artifact-item-title"></strong><span class="artifact-item-kind"></span><span class="artifact-item-description"></span></button>
    </template>
  `;
}

async function loadRunModules() {
  vi.resetModules();
  installRunsDom();
  const stateModule = await import('../wwwroot/js/state.js');
  const runsModule = await import('../wwwroot/js/runs.js');
  return { ...stateModule, ...runsModule };
}

function resetMocks() {
  [requestJsonMock, postPlanningFollowUpMock, renderComposerStateMock, collectRunRequestMock, canPauseActiveRunMock,
    resetStreamMock, showStreamStartingMock, closeEventStreamMock, connectEventStreamMock, syncSubmittedPromptSectionMock,
    applyPersistedRunEventsMock, renderTopbarMock, loadProjectsMock, syncKeepAwakeMock, saveShellStateMock, openModalMock,
    clearComposerAttachmentsMock, collectSubmissionAttachmentsMock, submitPlanApprovalMock].forEach(mock => mock.mockReset());
  wikiDocModeEnabled = false;
  canPauseActiveRunMock.mockImplementation(run => !!run?.isRunning && !!run?.runId && !!run?.runDirectory);
  collectSubmissionAttachmentsMock.mockReturnValue([]);
}

beforeEach(resetMocks);

describe('run screen behavior', () => {
  it('renders inactive and active run controls', async () => {
    const { state, elements, renderActiveRun } = await loadRunModules();

    renderActiveRun();
    expect(elements.pauseRun.disabled).toBe(true);
    expect(closeEventStreamMock).toHaveBeenCalled();
    expect(syncKeepAwakeMock).toHaveBeenCalledWith(false);

    state.activeRun = { runId: 'run-1', isRunning: true, status: RUN_STATUSES.RUNNING, runDirectory: 'C:/run', taskPrompt: 'Build it' };
    state.activeRunId = 'run-1';
    renderActiveRun();
    expect(elements.pauseRun.disabled).toBe(false);
    expect(elements.cancelRun.disabled).toBe(false);
    expect(syncKeepAwakeMock).toHaveBeenLastCalledWith(true);
    expect(syncSubmittedPromptSectionMock).toHaveBeenCalledWith('Build it');
  });

  it('submits a new run request and resets composer input', async () => {
    const { state, elements, submitRunRequest } = await loadRunModules();
    elements.taskPrompt.value = 'Change me';
    requestJsonMock.mockResolvedValueOnce({ runId: 'run-2', isRunning: true, status: RUN_STATUSES.RUNNING });

    await submitRunRequest({ taskPrompt: 'Change me' });

    expect(requestJsonMock).toHaveBeenCalledWith('/api/runs', expect.objectContaining({ method: 'POST' }));
    expect(state.activeRunId).toBe('run-2');
    expect(elements.taskPrompt.value).toBe('');
    expect(clearComposerAttachmentsMock).toHaveBeenCalledTimes(1);
    expect(saveShellStateMock).toHaveBeenCalled();
    expect(connectEventStreamMock).toHaveBeenCalled();
    expect(loadProjectsMock).toHaveBeenCalled();
  });

  it('sends planning follow-up through plan approval when approval is pending', async () => {
    const { state, elements, sendPlanningFollowUp } = await loadRunModules();
    state.projects = [{ projectId: 'project-1', workspacePath: 'C:/repo', runs: [{ runId: 'run-1' }] }];
    state.activeProjectId = 'project-1';
    state.activeRunId = 'run-1';
    state.selectedRunState = { workflow: WORKFLOWS.PLANNING };
    state.pendingInteraction = { kind: 'plan-approval' };
    collectSubmissionAttachmentsMock.mockReturnValue([{ id: 'img-1', kind: 'image' }]);
    elements.taskPrompt.value = 'Revise this plan';
    elements.planningFollowUp.textContent = 'Send';

    await sendPlanningFollowUp();

    expect(submitPlanApprovalMock).toHaveBeenCalledWith('regenerate', 'Revise this plan', [{ id: 'img-1', kind: 'image' }]);
    expect(postPlanningFollowUpMock).not.toHaveBeenCalled();
    expect(elements.taskPrompt.value).toBe('');
    expect(clearComposerAttachmentsMock).toHaveBeenCalledTimes(1);
  });

  it('posts planning follow-up messages with attachments for planning runs', async () => {
    const { state, elements, sendPlanningFollowUp } = await loadRunModules();
    state.projects = [{ projectId: 'project-1', workspacePath: 'C:/repo', runs: [{ runId: 'run-1' }] }];
    state.activeProjectId = 'project-1';
    state.activeRunId = 'run-1';
    state.selectedRunState = { workflow: WORKFLOWS.PLANNING, handoffRunId: null };
    collectSubmissionAttachmentsMock.mockReturnValue([{ id: 'img-1' }]);
    elements.taskPrompt.value = 'Add a migration step';
    elements.planningFollowUp.textContent = 'Send';

    await sendPlanningFollowUp();

    expect(postPlanningFollowUpMock).toHaveBeenCalledWith('run-1', {
      workspacePath: 'C:/repo',
      text: 'Add a migration step',
      attachments: [{ id: 'img-1' }],
      relatedRunId: 'run-1',
      kind: 'plan-revision'
    });
  });

  it('renders artifact list and selected preview', async () => {
    const { state, elements, renderArtifacts } = await loadRunModules();
    state.artifacts = [
      { fullPath: 'a.md', name: 'A', kind: 'Markdown', description: 'First', preview: 'A preview' },
      { fullPath: 'b.md', name: 'B', kind: 'Markdown', description: 'Second', preview: 'B preview' }
    ];

    renderArtifacts();
    expect(elements.artifactList.querySelectorAll('.artifact-item')).toHaveLength(2);
    expect(elements.artifactPreview.textContent).toBe('A preview');

    elements.artifactList.querySelectorAll('.artifact-item')[1].click();
    expect(state.selectedArtifactPath).toBe('b.md');
    expect(elements.artifactPreview.textContent).toBe('B preview');
  });
});