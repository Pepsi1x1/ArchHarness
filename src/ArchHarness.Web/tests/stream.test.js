import { beforeEach, describe, expect, it, vi } from 'vitest';

const requestJsonMock = vi.fn();
const renderTopbarMock = vi.fn();
const refreshActiveRunMock = vi.fn();
const loadProjectsMock = vi.fn();
const syncSelectedRunStateMock = vi.fn();

vi.mock('../wwwroot/js/api.js', () => ({
  requestJson: requestJsonMock
}));

vi.mock('../wwwroot/js/projects.js', () => ({
  loadProjects: loadProjectsMock,
  renderTopbar: renderTopbarMock
}));

vi.mock('../wwwroot/js/runs.js', () => ({
  refreshActiveRun: refreshActiveRunMock,
  syncSelectedRunStateToCurrentSelection: syncSelectedRunStateMock
}));

function installStreamDom() {
  document.body.innerHTML = `
    <div id="stream-empty"></div>
    <div id="stream-sections"></div>
  `;
}

async function loadStreamModules() {
  vi.resetModules();
  installStreamDom();
  const stateModule = await import('../wwwroot/js/state.js');
  const streamModule = await import('../wwwroot/js/stream.js');
  return { ...stateModule, ...streamModule };
}

function resetStreamState(state) {
  state.streamSections = {};
  state.streamOrder = [];
  state.streamAutoScroll = true;
  state.agentSpinningUp = {};
  state.eventSource = null;
  state.activeRun = null;
  state.activeRunId = null;
  state.selectedRunState = null;
  state.isUnloading = false;
}

beforeEach(() => {
  requestJsonMock.mockReset();
  renderTopbarMock.mockReset();
  refreshActiveRunMock.mockReset();
  loadProjectsMock.mockReset();
  syncSelectedRunStateMock.mockReset();
  vi.useFakeTimers();
});

describe('stream rendering', () => {
  it('groups assistant deltas by agent and sanitizes fallback HTML', async () => {
    const { state, elements, recordStreamEvent, renderStream } = await loadStreamModules();
    resetStreamState(state);

    recordStreamEvent({ agentId: 'frontend', agentRole: 'Frontend Developer', message: '<script>bad()</script>Hello' }, { deferRender: true });
    recordStreamEvent({ agentId: 'frontend', agentRole: 'Frontend Developer', message: ' world' }, { deferRender: true });
    renderStream();

    const section = elements.streamSections.querySelector('[data-agent-id="frontend"]');
    expect(section).not.toBeNull();
    expect(section.textContent).toContain('Hello world');
    expect(section.querySelector('script')).toBeNull();
    expect(elements.streamEmpty.classList.contains('hidden')).toBe(true);
  });

  it('keeps separate prompt turns for the same agent', async () => {
    const { state, recordStreamEvent } = await loadStreamModules();
    resetStreamState(state);

    recordStreamEvent({ agentId: 'planner', agentRole: 'Planning', streamKind: 'prompt', message: 'First prompt' }, { deferRender: true });
    recordStreamEvent({ agentId: 'planner', agentRole: 'Planning', streamKind: 'prompt', message: 'Second prompt' }, { deferRender: true });

    expect(state.streamOrder).toEqual(['planner', 'planner#2']);
    expect(state.streamSections.planner.segments[0].content).toBe('First prompt');
    expect(state.streamSections['planner#2'].segments[0].content).toBe('Second prompt');
  });

  it('renders tool calls as a collapsible group with friendly formatting', async () => {
    const { state, elements, recordStreamEvent, renderStream } = await loadStreamModules();
    resetStreamState(state);

    recordStreamEvent({ agentId: 'build', agentRole: 'Build', streamKind: 'tool-call', message: JSON.stringify({ name: 'runTests', args: { filter: 'Web' } }) }, { deferRender: true });
    recordStreamEvent({ agentId: 'build', agentRole: 'Build', streamKind: 'tool-call', message: 'plain command' }, { deferRender: true });
    renderStream();

    const details = elements.streamSections.querySelector('.stream-tool-calls');
    expect(details.querySelector('summary').textContent).toBe('Tool calls (2)');
    expect(details.textContent).toContain('runTests(filter: "Web")');
    expect(details.textContent).toContain('plain command');
  });

  it('shows starting for live empty runs and completed for persisted non-live runs', async () => {
    const { state, elements, applyPersistedRunEvents } = await loadStreamModules();
    resetStreamState(state);

    applyPersistedRunEvents([], { isLive: true });
    expect(elements.streamSections.querySelector('#stream-starting')?.textContent).toBe('Starting');

    applyPersistedRunEvents([
      { kind: 'agent-delta', agentId: 'security', agentRole: 'Security', message: 'Checked auth.' }
    ], { isLive: false });
    expect(elements.streamSections.querySelector('#stream-completed')?.textContent).toBe('Completed');
    expect(elements.streamSections.textContent).toContain('Checked auth.');
  });

  it('renders submitted prompts from persisted request events', async () => {
    const { state, elements, applyPersistedRunEvents } = await loadStreamModules();
    resetStreamState(state);

    applyPersistedRunEvents([
      { kind: 'request', taskPrompt: 'Implement the plan' }
    ], { isLive: false });

    expect(state.streamSections['submitted-run-prompt']).toBeTruthy();
    expect(elements.streamSections.textContent).toContain('Submitted Prompt');
    expect(elements.streamSections.textContent).toContain('Implement the plan');
  });
});