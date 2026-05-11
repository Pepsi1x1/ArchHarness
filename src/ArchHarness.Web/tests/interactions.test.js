import { beforeEach, describe, expect, it, vi } from 'vitest';

const requestJsonMock = vi.fn();
const renderTopbarMock = vi.fn();

vi.mock('../wwwroot/js/api.js', () => ({
  requestJson: requestJsonMock
}));

vi.mock('../wwwroot/js/composer.js', () => ({
  isPlanningModeEnabled: () => false
}));

vi.mock('../wwwroot/js/projects.js', () => ({
  renderTopbar: renderTopbarMock
}));

function installDom() {
  document.body.innerHTML = `
    <select id="run-mode"><option value="standard" selected>Standard</option></select>
    <section id="inline-interaction" class="inline-interaction hidden"></section>
    <div id="workspace-title"></div>
  `;
}

async function loadInteractionModules() {
  vi.resetModules();
  installDom();
  const stateModule = await import('../wwwroot/js/state.js');
  const interactionsModule = await import('../wwwroot/js/interactions.js');
  return { ...stateModule, ...interactionsModule };
}

function resetInteractionState(state) {
  state.pendingInteraction = null;
  state.pendingInteractionSignature = null;
  state.dismissedPendingInteractionSignature = null;
  state.pendingInteractionDraft = '';
  state.pendingInteractionDrafts = {};
  state.pendingPlanRevisionDraft = '';
  state.interactionPollHandle = null;
  state.pendingInteractionAbortController = null;
  state.pendingInteractionInFlight = false;
  state.isUnloading = false;
  state.selectedRunState = null;
}

function pendingUserInput(overrides = {}) {
  return {
    kind: 'user-input',
    question: 'Pick a direction',
    choices: ['Refine', 'Ship'],
    ...overrides
  };
}

async function flushPromises() {
  await Promise.resolve();
  await Promise.resolve();
}

beforeEach(() => {
  requestJsonMock.mockReset();
  renderTopbarMock.mockReset();
  vi.useFakeTimers();
});

describe('inline interactions', () => {
  it('renders a single user input with the padded layout hook and controls', async () => {
    const { state, elements, renderInlineInteraction } = await loadInteractionModules();
    resetInteractionState(state);
    state.pendingInteraction = pendingUserInput();

    renderInlineInteraction();

    expect(elements.inlineInteraction.classList.contains('hidden')).toBe(false);
    expect(elements.inlineInteraction.classList.contains('single-input')).toBe(true);
    expect(elements.inlineInteraction.classList.contains('question-batch')).toBe(false);
    expect(elements.inlineInteraction.querySelector('.inline-interaction-copy strong')?.textContent).toBe('Input');
    expect([...elements.inlineInteraction.querySelectorAll('.choice-chip')].map(button => button.textContent)).toEqual(['Refine', 'Ship']);
    expect(elements.inlineInteraction.querySelector('.inline-interaction-free-text textarea')).not.toBeNull();
    expect(elements.inlineInteraction.querySelector('.interaction-action.primary')?.textContent).toBe('Submit');
  });

  it('keeps the prompt dismissed when submit succeeds and the next poll still returns the same pending interaction', async () => {
    const { state, elements, renderInlineInteraction, clearPendingInteractionPoll } = await loadInteractionModules();
    resetInteractionState(state);
    const pending = pendingUserInput({ choices: [] });
    state.pendingInteraction = pending;
    state.pendingInteractionSignature = null;
    requestJsonMock
      .mockResolvedValueOnce({})
      .mockResolvedValueOnce(pending);

    renderInlineInteraction();
    elements.inlineInteraction.querySelector('textarea').value = 'Keep it simple';
    elements.inlineInteraction.querySelector('textarea').dispatchEvent(new Event('input', { bubbles: true }));
    elements.inlineInteraction.querySelector('.interaction-action.primary').click();
    await flushPromises();
    clearPendingInteractionPoll();

    expect(requestJsonMock).toHaveBeenNthCalledWith(1, '/api/interactions/user-input', expect.objectContaining({
      method: 'POST',
      body: JSON.stringify({ answer: 'Keep it simple' })
    }));
    expect(requestJsonMock).toHaveBeenNthCalledWith(2, '/api/interactions/pending', expect.objectContaining({ signal: expect.any(AbortSignal) }));
    expect(state.pendingInteraction).toBeNull();
    expect(elements.inlineInteraction.classList.contains('hidden')).toBe(true);
  });

  it('does not restore a submitted prompt after a stale no-pending conflict', async () => {
    const { state, elements, renderInlineInteraction, clearPendingInteractionPoll } = await loadInteractionModules();
    resetInteractionState(state);
    state.pendingInteraction = pendingUserInput({ choices: [] });
    const staleConflict = new Error('No pending user-input request is active.');
    staleConflict.status = 409;
    requestJsonMock.mockRejectedValueOnce(staleConflict);

    renderInlineInteraction();
    elements.inlineInteraction.querySelector('.interaction-action.primary').click();
    await flushPromises();
    clearPendingInteractionPoll();

    expect(requestJsonMock).toHaveBeenCalledTimes(1);
    expect(state.pendingInteraction).toBeNull();
    expect(elements.inlineInteraction.classList.contains('hidden')).toBe(true);
  });
});