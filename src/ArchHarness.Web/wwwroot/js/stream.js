import { STREAM_RENDER_DELAY_MS, STREAM_CONNECTION_STATES, DEFAULT_STREAM_EMPTY_MESSAGE } from './constants.js';
import { state, elements, isSelectedRunLive } from './state.js';
import { readEventField, formatTimestamp, escapeHtml, setSanitizedHtml } from './utils.js';
import { requestJson } from './api.js';
import { refreshActiveRun, syncSelectedRunStateToCurrentSelection } from './runs.js';
import { loadProjects, renderTopbar } from './projects.js';

const _agentTurnCounters = {};
const _agentCurrentSectionId = {};

function resolveSectionId(agentId, isNewTurn) {
  if (isNewTurn && _agentCurrentSectionId[agentId]) {
    const count = (_agentTurnCounters[agentId] || 1) + 1;
    _agentTurnCounters[agentId] = count;
    const sectionId = `${agentId}#${count}`;
    _agentCurrentSectionId[agentId] = sectionId;
  } else if (!_agentCurrentSectionId[agentId]) {
    _agentTurnCounters[agentId] = 1;
    _agentCurrentSectionId[agentId] = agentId;
  }
  return _agentCurrentSectionId[agentId];
}

export function ensureStreamSection(sectionId, agentRole, title) {
  if (!state.streamSections[sectionId]) {
    state.streamSections[sectionId] = {
      agentId: sectionId,
      agentRole,
      title: title || agentRole,
      segments: [],
      updatedAt: null,
      segmentCount: 0,
      streamKind: "assistant",
      renderHandle: null,
      renderVersion: 0
    };
    state.streamOrder.push(sectionId);
  }

  const section = state.streamSections[sectionId];
  section.agentRole = agentRole || section.agentRole;
  section.title = title || section.title || section.agentRole;
  return section;
}

function getOrCreateTextSegment(section) {
  const last = section.segments[section.segments.length - 1];
  if (last?.type === "text") return last;
  const seg = { type: "text", content: "", html: "" };
  section.segments.push(seg);
  return seg;
}

function createPromptSegment(section, content) {
  const seg = { type: "prompt", content, html: "" };
  section.segments.push(seg);
  return seg;
}

function getOrCreateToolGroup(section) {
  const last = section.segments[section.segments.length - 1];
  if (last?.type === "tool-group") return last;
  const seg = { type: "tool-group", calls: [] };
  section.segments.push(seg);
  return seg;
}

function getOrCreateReasoningSegment(section) {
  const last = section.segments[section.segments.length - 1];
  if (last?.type === "reasoning") return last;
  const seg = { type: "reasoning", content: "", html: "" };
  section.segments.push(seg);
  return seg;
}

function formatToolCall(call) {
  try {
    const parsed = JSON.parse(call);
    if (parsed && typeof parsed.name === "string") {
      if (parsed.args && typeof parsed.args === "object") {
        const argStr = Object.entries(parsed.args)
          .map(([k, v]) => `${k}: ${JSON.stringify(v)}`)
          .join(", ");
        return argStr ? `${parsed.name}(${argStr})` : parsed.name;
      }
      return parsed.name;
    }
  } catch {
    // fall through to plain text
  }
  return call;
}

function renderToolGroupHtml(seg) {
  const count = seg.calls.length;
  const label = count === 1 ? "Tool call" : "Tool calls";
  const items = seg.calls.map(call =>
    `<li class="stream-tool-call-item">${escapeHtml(formatToolCall(call))}</li>`).join("");
  return `<details class="stream-tool-calls"><summary class="stream-tool-calls-header">${label} (${count})</summary><ul class="stream-tool-calls-list">${items}</ul></details>`;
}

function captureDetailsState(container) {
  const map = new Map();
  if (!container) return map;
  container.querySelectorAll("details").forEach((el, i) => {
    const key = el.className + ":" + i;
    map.set(key, el.open);
  });
  return map;
}

function restoreDetailsState(container, map) {
  if (!container || !map.size) return;
  container.querySelectorAll("details").forEach((el, i) => {
    const key = el.className + ":" + i;
    if (map.has(key)) el.open = map.get(key);
  });
}

function notifyStreamRendered() {
  globalThis.dispatchEvent(new CustomEvent("archharness:stream-rendered"));
}

function buildSectionBodyHtml(section) {
  if (section.segments.length === 0) {
    return `<pre>Waiting for rendered markdown...</pre>`;
  }
  return section.segments.map(seg => {
    if (seg.type === "tool-group") return renderToolGroupHtml(seg);
    if (seg.type === "prompt") {
      const content = seg.html || (seg.content ? `<pre>${escapeHtml(seg.content)}</pre>` : "");
      return `<section class="stream-prompt-block"><div class="stream-prompt-label">Prompt</div>${content}</section>`;
    }
    if (seg.type === "reasoning") {
      const content = seg.html || (seg.content ? `<pre>${escapeHtml(seg.content)}</pre>` : "");
      return `<details class="stream-reasoning-block"><summary class="stream-reasoning-label">Reasoning</summary><div class="stream-reasoning-content">${content}</div></details>`;
    }
    return seg.html || (seg.content ? `<pre>${escapeHtml(seg.content)}</pre>` : "");
  }).join("");
}

export function renderStream() {
  elements.streamSections.replaceChildren();
  const sections = state.streamOrder.map(agentId => state.streamSections[agentId]).filter(Boolean);
  const hasSections = sections.length > 0;
  elements.streamEmpty.classList.toggle("hidden", hasSections);
  if (hasSections) hideStreamStarting();

  sections.forEach(section => {
    const details = document.createElement("details");
    details.className = "stream-section";
    details.open = true;

    const summary = document.createElement("summary");
    summary.className = "stream-section-summary";

    const summaryLeft = document.createElement("div");
    const summaryTitle = document.createElement("strong");
    summaryTitle.textContent = section.title || section.agentRole;
    const summaryRole = document.createElement("span");
    summaryRole.textContent = section.agentRole || "agent";
    summaryLeft.append(summaryTitle, summaryRole);

    const summaryMeta = document.createElement("div");
    summaryMeta.className = "stream-section-meta";
    const summaryTime = document.createElement("span");
    summaryTime.textContent = formatTimestamp(section.updatedAt);
    summaryMeta.append(summaryTime);

    summary.append(summaryLeft, summaryMeta);

    const body = document.createElement("div");
    body.className = "markdown-surface stream-markdown";
    body.dataset.agentId = section.agentId;
    setSanitizedHtml(body, buildSectionBodyHtml(section));
    details.append(summary, body);
    elements.streamSections.append(details);
  });

  scrollStreamToBottom();
  renderTopbar();
  notifyStreamRendered();
}

export function syncSubmittedPromptSection(promptText) {
  const submittedPrompt = String(promptText || "").trim();
  if (!submittedPrompt) {
    return;
  }

  const section = ensureStreamSection("submitted-run-prompt", "Run Request", "Submitted Prompt");
  section.segments = [
    {
      type: "text",
      content: submittedPrompt,
      html: ""
    }
  ];
  section.updatedAt = new Date().toISOString();
  section.segmentCount = 1;
  section.streamKind = "prompt";
  renderStream();
  scheduleStreamRender(section.agentId);
}

export function scrollStreamToBottom() {
  if (!state.streamAutoScroll) return;
  const el = elements.streamSections;
  el.scrollTop = el.scrollHeight;
}

export function showStreamStarting() {
  state.streamAutoScroll = true;
  const el = document.createElement("div");
  el.id = "stream-starting";
  el.className = "stream-starting";
  el.textContent = "Starting";
  elements.streamSections.append(el);
}

export function hideStreamStarting() {
  const el = elements.streamSections.querySelector("#stream-starting");
  if (el) el.remove();
}

function agentDisplayName(source) {
  const names = {
    "backendDeveloper": "Backend Developer",
    "backend-developer": "Backend Developer",
    "BackendDeveloper": "Backend Developer",
    "frontendDeveloper": "Frontend Developer",
    "frontend-developer": "Frontend Developer",
    "FrontendDeveloper": "Frontend Developer",
    "codingStyle": "Coding Style",
    "coding-style": "Coding Style",
    "CodingStyle": "Coding Style",
    "security": "Security",
    "Security": "Security",
    "architecture": "Architecture",
    "Architecture": "Architecture",
    "orchestration": "Orchestration",
    "Orchestration": "Orchestration",
    "planning": "Planning",
    "Planning": "Planning",
    "build": "Build",
    "Build": "Build"
  };
  return names[source] || source;
}

export function showAgentSpinningUp(source) {
  const key = source.toLowerCase();
  if (state.agentSpinningUp[key]?.parentNode) {
    return;
  }
  const el = document.createElement("div");
  el.className = "stream-starting stream-agent-spinning-up";
  el.dataset.spinningKey = key;
  el.textContent = `Spinning up ${agentDisplayName(source)}`;
  state.agentSpinningUp[key] = el;
  elements.streamSections.append(el);
  scrollStreamToBottom();
}

function hideAgentSpinningUp(source) {
  const key = source.toLowerCase();
  const el = state.agentSpinningUp[key];
  if (el) {
    el.remove();
    delete state.agentSpinningUp[key];
  }
}

export function showStreamCompleted() {
  const existing = elements.streamSections.querySelector("#stream-completed");
  if (existing) return;
  const el = document.createElement("div");
  el.id = "stream-completed";
  el.className = "stream-completed";
  el.textContent = "Completed";
  elements.streamSections.append(el);
  scrollStreamToBottom();
}

let _pendingRenderAgentIds = new Set();
let _pendingRenderFrame = null;

function flushPendingRenders() {
  _pendingRenderFrame = null;
  const agentIds = _pendingRenderAgentIds;
  _pendingRenderAgentIds = new Set();

  let needsFullRender = false;
  for (const agentId of agentIds) {
    const section = state.streamSections[agentId];
    if (!section) continue;
    const container = elements.streamSections.querySelector(`[data-agent-id="${CSS.escape(agentId)}"]`);
    if (!container) {
      needsFullRender = true;
      break;
    }
    const openState = captureDetailsState(container);
    setSanitizedHtml(container, buildSectionBodyHtml(section));
    restoreDetailsState(container, openState);
  }

  if (needsFullRender) {
    renderStream();
  }

  scrollStreamToBottom();

  for (const agentId of agentIds) {
    scheduleStreamRender(agentId);
  }

  notifyStreamRendered();
}

export function scheduleIncrementalRender(agentId) {
  _pendingRenderAgentIds.add(agentId);
  if (!_pendingRenderFrame) {
    _pendingRenderFrame = globalThis.requestAnimationFrame
      ? globalThis.requestAnimationFrame(flushPendingRenders)
      : globalThis.setTimeout(flushPendingRenders, 16);
  }
}

export function scheduleStreamRender(agentId) {
  const section = state.streamSections[agentId];
  if (!section) {
    return;
  }

  if (section.renderHandle) {
    globalThis.clearTimeout(section.renderHandle);
  }

  section.renderHandle = globalThis.setTimeout(() => {
    section.renderHandle = null;
    void renderStreamSectionMarkdown(agentId);
  }, STREAM_RENDER_DELAY_MS);
}

async function renderStreamSectionMarkdown(agentId) {
  const section = state.streamSections[agentId];
  if (!section) {
    return;
  }

  const version = ++section.renderVersion;
  const textSegments = section.segments.filter(s => (s.type === "text" || s.type === "prompt" || s.type === "reasoning") && s.content);

  const renderedSegments = await Promise.all(textSegments.map(async seg => {
    try {
      const response = await requestJson("/api/markdown/render", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ markdown: seg.content })
      });
      return response.html || `<pre>${escapeHtml(seg.content)}</pre>`;
    } catch {
      return `<pre>${escapeHtml(seg.content)}</pre>`;
    }
  }));

  if (state.streamSections[agentId]?.renderVersion !== version) {
    return;
  }

  textSegments.forEach((seg, index) => {
    seg.html = renderedSegments[index];
  });

  const container = elements.streamSections.querySelector(`[data-agent-id="${CSS.escape(agentId)}"]`);
  if (container) {
    const openState = captureDetailsState(container);
    setSanitizedHtml(container, buildSectionBodyHtml(section));
    restoreDetailsState(container, openState);
    scrollStreamToBottom();
    notifyStreamRendered();
  } else {
    renderStream();
  }
}

export function recordStreamEvent(entry, options = {}) {
  const deferRender = options.deferRender === true;
  const agentId = readEventField(entry, "agentId");
  if (!agentId) {
    return;
  }

  const agentRole = readEventField(entry, "agentRole") || readEventField(entry, "source") || "unknown";
  const message = readEventField(entry, "message") || "";
  if (!message) {
    return;
  }

  const streamKind = readEventField(entry, "streamKind") || "assistant";
  const isNewTurn = streamKind === "prompt";
  const sectionId = resolveSectionId(agentId, isNewTurn);
  const title = streamKind === "tool-call" || streamKind === "prompt" || streamKind === "reasoning" ? null : readEventField(entry, "title");
  const section = ensureStreamSection(sectionId, agentRole, title);
  if (section.segmentCount === 0) {
    hideAgentSpinningUp(agentRole);
  }

  if (streamKind === "tool-call") {
    const group = getOrCreateToolGroup(section);
    group.calls.push(message);
    section.segmentCount += 1;
    section.updatedAt = readEventField(entry, "timestampUtc") || new Date().toISOString();
    if (!deferRender) {
      scheduleIncrementalRender(sectionId);
    }
    return;
  }

  if (streamKind === "prompt") {
    createPromptSegment(section, message);
    section.segmentCount += 1;
    section.updatedAt = readEventField(entry, "timestampUtc") || new Date().toISOString();
    section.streamKind = streamKind;

    if (!deferRender) {
      scheduleIncrementalRender(sectionId);
    }
    return;
  }

  if (streamKind === "reasoning") {
    const seg = getOrCreateReasoningSegment(section);
    seg.content += message;
    section.segmentCount += 1;
    section.updatedAt = readEventField(entry, "timestampUtc") || new Date().toISOString();
    if (!deferRender) {
      scheduleIncrementalRender(sectionId);
    }
    return;
  }

  const seg = getOrCreateTextSegment(section);
  seg.content += message;
  section.segmentCount += 1;
  section.updatedAt = readEventField(entry, "timestampUtc") || new Date().toISOString();
  section.streamKind = streamKind;

  if (!deferRender) {
    scheduleIncrementalRender(sectionId);
  }
}

export function resetStream() {
  Object.values(state.streamSections).forEach(section => {
    if (section.renderHandle) {
      globalThis.clearTimeout(section.renderHandle);
    }
  });
  if (_pendingRenderFrame) {
    (globalThis.cancelAnimationFrame || globalThis.clearTimeout)(_pendingRenderFrame);
    _pendingRenderFrame = null;
    _pendingRenderAgentIds.clear();
  }
  state.streamSections = {};
  state.streamOrder = [];
  Object.keys(_agentTurnCounters).forEach(k => delete _agentTurnCounters[k]);
  Object.keys(_agentCurrentSectionId).forEach(k => delete _agentCurrentSectionId[k]);
  Object.keys(state.agentSpinningUp).forEach(key => {
    const el = state.agentSpinningUp[key];
    if (el?.parentNode) el.remove();
  });
  state.agentSpinningUp = {};
  renderStream();
}

export function applyPersistedRunEvents(events, options = {}) {
  const isLive = options.isLive === true;
  resetStream();

  let submittedPrompt = null;
  (Array.isArray(events) ? events : []).forEach(entry => {
    const kind = readEventField(entry, "kind") || "";
    if (kind === "request") {
      submittedPrompt = readEventField(entry, "taskPrompt") || submittedPrompt;
      return;
    }

    if (kind === "agent-delta") {
      recordStreamEvent(entry, { deferRender: true });
    }
  });

  if (submittedPrompt) {
    syncSubmittedPromptSection(submittedPrompt);
  }

  renderStream();
  state.streamOrder.forEach(agentId => {
    scheduleStreamRender(agentId);
  });

  if (state.streamOrder.length > 0) {
    if (isLive) {
      scrollStreamToBottom();
    } else {
      showStreamCompleted();
    }
  } else if (isLive) {
    showStreamStarting();
  } else {
    elements.streamEmpty.textContent = DEFAULT_STREAM_EMPTY_MESSAGE;
    renderStream();
  }
}

export function closeEventStream(status = STREAM_CONNECTION_STATES.IDLE) {
  if (state.eventSource) {
    state.eventSource.close();
    state.eventSource = null;
  }
  if (status === STREAM_CONNECTION_STATES.IDLE && state.streamOrder.length > 0) {
    showStreamCompleted();
  }
}

export function connectEventStream() {
  if (state.eventSource || !state.activeRun?.isRunning || !isSelectedRunLive()) {
    return;
  }

  const eventSource = new EventSource("/api/runs/active/events");
  state.eventSource = eventSource;
  let sidebarRefreshed = false;
  let refreshHandle = null;
  let refreshInFlight = false;

  const throttledRefresh = () => {
    if (refreshHandle || refreshInFlight) return;
    refreshHandle = globalThis.setTimeout(async () => {
      refreshHandle = null;
      refreshInFlight = true;
      try {
        const snapshot = await refreshActiveRun();
        if (!snapshot?.isRunning) {
          closeEventStream(STREAM_CONNECTION_STATES.IDLE);
          await loadProjects();
          await syncSelectedRunStateToCurrentSelection();
        } else if (!sidebarRefreshed && snapshot?.runId) {
          sidebarRefreshed = true;
          await loadProjects();
          await syncSelectedRunStateToCurrentSelection();
        }
      } finally {
        refreshInFlight = false;
      }
    }, 500);
  };

  const onEvent = event => {
    const payload = JSON.parse(event.data);
    const kind = readEventField(payload, "kind") || "";
    if (kind === "agent-delta") {
      recordStreamEvent(payload);
    } else if (kind === "runtime-progress") {
      const message = readEventField(payload, "message") || "";
      const source = readEventField(payload, "source") || "";
      const details = readEventField(payload, "details") || "";
      if (source === "Planning" && message === "Plan review ready" && details) {
        const planReviewAgentId = readEventField(payload, "agentId") || `planning-review-${state.activeRun?.runId || "active"}`;
        recordStreamEvent({
          ...payload,
          kind: "agent-delta",
          agentId: planReviewAgentId,
          agentRole: "Planning",
          message: details,
          contentFormat: "markdown",
          streamKind: "assistant",
          title: "Plan Review"
        });
      }
      if (message.endsWith("prompt started") && source) {
        showAgentSpinningUp(source);
      }
    }

    throttledRefresh();
  };

  eventSource.onmessage = onEvent;
  ["run-state", "runtime-progress", "agent-delta", "copilot-session"].forEach(kind => {
    eventSource.addEventListener(kind, onEvent);
  });

  eventSource.onerror = () => {
    if (state.isUnloading) {
      return;
    }

    closeEventStream(state.activeRun?.isRunning ? STREAM_CONNECTION_STATES.RECONNECTING : STREAM_CONNECTION_STATES.IDLE);
    if (state.activeRun?.isRunning) {
      globalThis.setTimeout(connectEventStream, 1000);
    }
  };
}
