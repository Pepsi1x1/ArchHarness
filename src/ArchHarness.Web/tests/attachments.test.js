import { beforeEach, describe, expect, it, vi } from 'vitest';

const renderComposerStateMock = vi.fn();

vi.mock('../wwwroot/js/composer.js', () => ({
  renderComposerState: renderComposerStateMock
}));

function installAttachmentDom() {
  document.body.innerHTML = `
    <textarea id="task-prompt"></textarea>
    <div id="prompt-attachments"></div>
    <input id="prompt-attachment-input" type="file">
    <button id="prompt-attachment-button" type="button"></button>
  `;
}

async function loadAttachmentModules() {
  vi.resetModules();
  installAttachmentDom();
  const stateModule = await import('../wwwroot/js/state.js');
  const attachmentsModule = await import('../wwwroot/js/attachments.js');
  return { ...stateModule, ...attachmentsModule };
}

function resetAttachmentState(state) {
  state.composerAttachments = [];
}

beforeEach(() => {
  renderComposerStateMock.mockReset();
});

describe('composer attachments', () => {
  it('renders attachment chips and excludes preview URLs from submission payloads', async () => {
    const { state, elements, renderAttachments, collectSubmissionAttachments } = await loadAttachmentModules();
    resetAttachmentState(state);
    state.composerAttachments = [{
      id: 'img-1',
      kind: 'image',
      mimeType: 'image/png',
      fileName: 'diagram.png',
      sizeBytes: 12,
      dataBase64: 'ZmFrZQ==',
      previewUrl: 'data:image/png;base64,ZmFrZQ=='
    }];

    renderAttachments();

    expect(elements.promptAttachments.classList.contains('hidden')).toBe(false);
    expect(elements.promptAttachments.querySelector('.attachment-chip-label').textContent).toBe('diagram.png');
    expect(elements.promptAttachments.querySelector('img').src).toContain('data:image/png;base64,ZmFrZQ==');
    expect(collectSubmissionAttachments()).toEqual([{ id: 'img-1', kind: 'image', mimeType: 'image/png', fileName: 'diagram.png', sizeBytes: 12, dataBase64: 'ZmFrZQ==' }]);
  });

  it('removes an attachment through its chip button and rerenders composer state', async () => {
    const { state, elements, renderAttachments } = await loadAttachmentModules();
    resetAttachmentState(state);
    state.composerAttachments = [
      { id: 'keep', kind: 'image', mimeType: 'image/png', fileName: 'keep.png', sizeBytes: 1, dataBase64: 'a', previewUrl: 'data:image/png;base64,a' },
      { id: 'remove', kind: 'image', mimeType: 'image/png', fileName: 'remove.png', sizeBytes: 1, dataBase64: 'b', previewUrl: 'data:image/png;base64,b' }
    ];

    renderAttachments();
    elements.promptAttachments.querySelector('[aria-label="Remove remove.png"]').click();

    expect(state.composerAttachments.map(item => item.id)).toEqual(['keep']);
    expect(elements.promptAttachments.textContent).toContain('keep.png');
    expect(elements.promptAttachments.textContent).not.toContain('remove.png');
    expect(renderComposerStateMock).toHaveBeenCalledTimes(1);
  });

  it('adds only supported image files and respects the attachment limit', async () => {
    const { state, addComposerFiles } = await loadAttachmentModules();
    resetAttachmentState(state);
    state.composerAttachments = Array.from({ length: 5 }, (_, index) => ({
      id: `existing-${index}`,
      kind: 'image',
      mimeType: 'image/png',
      fileName: `existing-${index}.png`,
      sizeBytes: 1,
      dataBase64: 'a',
      previewUrl: 'data:image/png;base64,a'
    }));

    const image = new File(['hello'], 'new.png', { type: 'image/png' });
    const text = new File(['hello'], 'notes.txt', { type: 'text/plain' });
    await addComposerFiles([text, image, new File(['ignored'], 'second.png', { type: 'image/png' })]);

    expect(state.composerAttachments).toHaveLength(6);
    expect(state.composerAttachments.at(-1).fileName).toBe('new.png');
    expect(state.composerAttachments.some(item => item.fileName === 'notes.txt')).toBe(false);
    expect(renderComposerStateMock).toHaveBeenCalledTimes(1);
  });
});