import { beforeEach, describe, expect, it, vi } from 'vitest';

const requestJsonMock = vi.fn();
const closeModalMock = vi.fn();
let planningModeEnabled = false;

vi.mock('../wwwroot/js/api.js', () => ({
  requestJson: requestJsonMock
}));

vi.mock('../wwwroot/js/modals.js', () => ({
  closeModal: closeModalMock
}));

vi.mock('../wwwroot/js/composer.js', () => ({
  isPlanningModeEnabled: () => planningModeEnabled
}));

function installSettingsDom() {
  document.body.innerHTML = `
    <select id="permission-mode"><option value="ask">ask</option><option value="approve-all">approve-all</option></select>
    <select id="run-mode"><option value="standard">standard</option><option value="architecture-review">architecture-review</option><option value="planning">planning</option></select>
    <div id="settings-permission-mode-wrap"></div>
    <input id="settings-architecture-mode" type="checkbox">
    <textarea id="settings-architecture-prompt"></textarea>
    <input id="settings-wikidoc-parallelism" type="number">
    <div id="settings-grid"></div>
    <button class="settings-tab" data-tab="agent-settings"></button>
    <button class="settings-tab" data-tab="source-control-providers"></button>
    <section id="settings-tab-agent"></section>
    <section id="settings-tab-providers" class="hidden"></section>
  `;
}

async function loadSettingsModules() {
  vi.resetModules();
  installSettingsDom();
  const stateModule = await import('../wwwroot/js/state.js');
  const settingsModule = await import('../wwwroot/js/settings.js');
  return { ...stateModule, ...settingsModule };
}

function sampleSettings() {
  return {
    agentModels: { planning: 'gpt-5', wikidoc: 'mini' },
    agentReasoningEfforts: { planning: 'high', wikidoc: '' },
    defaults: {
      permissionHandlerMode: 'approve-all',
      architectureReviewMode: true,
      architectureReviewPrompt: 'Review boundaries',
      wikidocParallelism: 7
    }
  };
}

beforeEach(() => {
  requestJsonMock.mockReset();
  closeModalMock.mockReset();
  planningModeEnabled = false;
});

describe('settings screen', () => {
  it('loads settings, renders model dropdowns, and applies defaults', async () => {
    const { state, elements, loadSettings } = await loadSettingsModules();
    state.bootstrap = { permissionModes: ['ask', 'approve-all'] };
    requestJsonMock
      .mockResolvedValueOnce(sampleSettings())
      .mockResolvedValueOnce({ models: [
        { modelId: 'gpt-5', displayName: 'GPT-5', costBand: 'High', supportedReasoningEfforts: ['low', 'high'], defaultReasoningEffort: 'low' },
        { modelId: 'mini', displayName: 'Mini', supportedReasoningEfforts: [] }
      ] });

    await loadSettings();

    expect(requestJsonMock).toHaveBeenNthCalledWith(1, '/api/settings');
    expect(requestJsonMock).toHaveBeenNthCalledWith(2, '/api/models');
    expect(elements.permissionMode.value).toBe('approve-all');
    expect(elements.runMode.value).toBe('architecture-review');
    expect(elements.settingsArchitectureMode.checked).toBe(true);
    expect(elements.settingsArchitecturePrompt.value).toBe('Review boundaries');
    expect(elements.settingsWikidocParallelism.value).toBe('7');
    expect(elements.settingsGrid.querySelectorAll('[data-dropdown-id^="settings-model-"]').length).toBeGreaterThan(5);
    expect(elements.settingsGrid.textContent).toContain('Planning');
  });

  it('does not override planning mode while applying defaults', async () => {
    const { state, elements, applySettingsDefaults } = await loadSettingsModules();
    state.settings = sampleSettings();
    planningModeEnabled = true;
    elements.runMode.value = 'planning';

    applySettingsDefaults();

    expect(elements.runMode.value).toBe('planning');
  });

  it('saves current dropdown selections and closes the modal', async () => {
    const { state, elements, renderSettingsForm, populateSettingsPermissionMode, saveSettings } = await loadSettingsModules();
    state.bootstrap = { permissionModes: ['ask', 'approve-all'] };
    state.models = [{ modelId: 'gpt-5', displayName: 'GPT-5', supportedReasoningEfforts: ['high'], defaultReasoningEffort: 'low' }];
    state.settings = sampleSettings();
    renderSettingsForm();
    populateSettingsPermissionMode();
    elements.settingsArchitectureMode.checked = false;
    elements.settingsArchitecturePrompt.value = '  Keep it quiet  ';
    elements.settingsWikidocParallelism.value = '3';
    requestJsonMock.mockResolvedValueOnce(sampleSettings());

    await saveSettings({ preventDefault: vi.fn() });

    expect(requestJsonMock).toHaveBeenCalledWith('/api/settings', expect.objectContaining({
      method: 'PUT',
      body: expect.stringContaining('Keep it quiet')
    }));
    const body = JSON.parse(requestJsonMock.mock.calls[0][1].body);
    expect(body.defaults).toEqual({ permissionHandlerMode: 'approve-all', architectureReviewMode: false, architectureReviewPrompt: 'Keep it quiet', wikidocParallelism: 3 });
    expect(closeModalMock).toHaveBeenCalledTimes(1);
  });

  it('switches settings tabs and supports keyboard navigation', async () => {
    const { switchSettingsTab, handleSettingsTabKeydown } = await loadSettingsModules();
    const tabs = [...document.querySelectorAll('.settings-tab')];
    tabs[1].focus = vi.fn();

    switchSettingsTab('source-control-providers');
    expect(tabs[1].classList.contains('active')).toBe(true);
    expect(document.getElementById('settings-tab-agent').hidden).toBe(true);

    handleSettingsTabKeydown({ currentTarget: tabs[0], key: 'ArrowRight', preventDefault: vi.fn() });
    expect(tabs[1].focus).toHaveBeenCalledTimes(1);
  });
});