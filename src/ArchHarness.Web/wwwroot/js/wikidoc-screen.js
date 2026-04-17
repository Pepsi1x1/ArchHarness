/**
 * Self-contained logic for the dedicated wikidoc Electron screen.
 * Communicates with the existing active-run HTTP API and SSE stream —
 * no new transport or host is introduced.
 */

import { requestJson } from './api.js';
import { readEventField, escapeHtml, setSanitizedHtml } from './utils.js';
import { desktopBridge, selectFolderWithDesktopBridge } from './desktop-bridge.js';

const WIKIDOC_WORKFLOW = "wikidoc";
const WIKIDOC_DEFAULT_PROMPT = "Generate comprehensive wiki documentation for this workspace.";
const STREAM_RENDER_DELAY_MS = 140;

// ── DOM refs ──────────────────────────────────────────────────────────────────

const folderInput = document.getElementById("wikidoc-folder");
const browseButton = document.getElementById("wikidoc-browse");
const promptInput = document.getElementById("wikidoc-prompt");
const generateButton = document.getElementById("wikidoc-generate");
const resumeButton = document.getElementById("wikidoc-resume");
const statusEl = document.getElementById("wikidoc-status");
const streamEmptyEl = document.getElementById("wikidoc-stream-empty");
const streamSectionsEl = document.getElementById("wikidoc-stream-sections");
const progressPanel = document.getElementById("wikidoc-progress");
const progressCounter = document.getElementById("wikidoc-progress-counter");
const progressBar = document.getElementById("wikidoc-progress-bar");
const activeAgentsEl = document.getElementById("wikidoc-active-agents");

// ── State ─────────────────────────────────────────────────────────────────────

let eventSource = null;
let activeRunId = null;
let isRunning = false;
let streamSections = {};
let streamOrder = [];
let streamAutoScroll = true;
let refreshHandle = null;
let resumableRun = null;
let progressTotal = 0;
let progressDone = 0;
let activeAgents = new Map(); // repoName → startTime

// ── Folder browsing ───────────────────────────────────────────────────────────

async function browseScanRoot() {
  const selected = await selectFolderWithDesktopBridge({
    title: "Select Scan Root Folder",
    unavailableMessage: "",
    unavailableTarget: null
  });
  if (selected) {
    folderInput.value = selected;
    void checkForResumableRun();
  }
}

// ── Run launch ────────────────────────────────────────────────────────────────

async function startWikiDocRun() {
  const scanRoot = folderInput.value.trim();
  if (!scanRoot) {
    setStatus("Enter a scan root folder path.", "error");
    return;
  }

  const prompt = promptInput.value.trim() || WIKIDOC_DEFAULT_PROMPT;

  generateButton.disabled = true;
  generateButton.textContent = "Generating…";
  hideResumeButton();
  setStatus("Starting run…");

  resetStream();
  showStreamStarting();

  try {
    const snapshot = await requestJson("/api/runs", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        taskPrompt: prompt,
        workspacePath: scanRoot,
        workspaceMode: "existing-folder",
        workflow: WIKIDOC_WORKFLOW,
        projectName: null,
        projectId: null,
        modelOverrides: null,
        buildCommand: null,
        permissionHandlerMode: "approve-all",
        reviewLoopAgents: null,
        architectureLoopMode: false,
        architectureLoopPrompt: null
      })
    });

    activeRunId = snapshot?.runId || null;
    isRunning = true;
    setStatus("Running…");
    connectEventStream();
  } catch (error) {
    generateButton.disabled = false;
    generateButton.textContent = "Generate";
    setStatus(`Failed to start: ${error.message || "Unknown error"}`, "error");
    hideStreamStarting();
  }
}

// ── Resume detection ──────────────────────────────────────────────────────────

async function checkForResumableRun() {
  const scanRoot = folderInput.value.trim();
  if (!scanRoot) {
    hideResumeButton();
    return;
  }

  try {
    const runs = await requestJson(`/api/runs?workspacePath=${encodeURIComponent(scanRoot)}&maxCount=5`);
    if (!Array.isArray(runs) || runs.length === 0) {
      hideResumeButton();
      return;
    }

    for (const run of runs) {
      try {
        const state = await requestJson(
          `/api/runs/${encodeURIComponent(run.runId)}/state?workspacePath=${encodeURIComponent(scanRoot)}`
        );
        if (state?.workflow === WIKIDOC_WORKFLOW && state?.canResume) {
          resumableRun = { runId: run.runId, workspacePath: scanRoot };
          showResumeButton();
          return;
        }
      } catch {
        // Skip runs whose state cannot be read.
      }
    }

    hideResumeButton();
  } catch {
    hideResumeButton();
  }
}

function showResumeButton() {
  resumeButton.classList.remove("hidden");
}

function hideResumeButton() {
  resumableRun = null;
  resumeButton.classList.add("hidden");
}

async function resumeWikiDocRun() {
  if (!resumableRun) return;

  const { runId, workspacePath } = resumableRun;
  resumeButton.disabled = true;
  resumeButton.textContent = "Resuming…";
  generateButton.disabled = true;
  setStatus("Resuming run…");

  resetStream();
  showStreamStarting();

  try {
    const snapshot = await requestJson(
      `/api/runs/${encodeURIComponent(runId)}/resume?workspacePath=${encodeURIComponent(workspacePath)}`,
      { method: "POST" }
    );

    activeRunId = snapshot?.runId || runId;
    isRunning = true;
    hideResumeButton();
    setStatus("Running… (resumed)");
    connectEventStream();
  } catch (error) {
    resumeButton.disabled = false;
    resumeButton.textContent = "Resume";
    generateButton.disabled = false;
    setStatus(`Failed to resume: ${error.message || "Unknown error"}`, "error");
    hideStreamStarting();
  }
}

// ── SSE stream ────────────────────────────────────────────────────────────────

function connectEventStream() {
  if (eventSource) {
    eventSource.close();
    eventSource = null;
  }

  const es = new EventSource("/api/runs/active/events");
  eventSource = es;

  const onEvent = event => {
    let payload;
    try {
      payload = JSON.parse(event.data);
    } catch {
      return;
    }

    const kind = readEventField(payload, "kind") || "";

    if (kind === "agent-delta") {
      recordStreamEvent(payload);
    } else if (kind === "runtime-progress") {
      const message = readEventField(payload, "message") || "";
      const source = readEventField(payload, "source") || "";
      if (message.startsWith("wikidoc:")) {
        handleWikiDocProgress(message);
      } else if (message.endsWith("prompt started") && source) {
        showAgentSpinningUp(source);
      }
    }

    scheduleActiveRunRefresh();
  };

  es.onmessage = onEvent;
  ["run-state", "runtime-progress", "agent-delta", "copilot-session"].forEach(kind => {
    es.addEventListener(kind, onEvent);
  });

  es.onerror = () => {
    if (!isRunning) return;
    closeEventStream();
    setTimeout(connectEventStream, 1000);
  };
}

function closeEventStream() {
  if (eventSource) {
    eventSource.close();
    eventSource = null;
  }
}

// ── Active-run polling ────────────────────────────────────────────────────────

function scheduleActiveRunRefresh() {
  if (refreshHandle) return;
  refreshHandle = setTimeout(async () => {
    refreshHandle = null;
    try {
      const snapshot = await requestJson("/api/runs/active");
      if (!snapshot?.isRunning) {
        onRunFinished(snapshot);
      }
    } catch {
      // transient error — stream will retry on its own
    }
  }, 500);
}

function onRunFinished(snapshot) {
  isRunning = false;
  closeEventStream();
  showStreamCompleted();
  generateButton.disabled = false;
  generateButton.textContent = "Generate";
  resumeButton.disabled = false;
  resumeButton.textContent = "Resume";
  const status = snapshot?.status || "completed";
  setStatus(status === "completed" ? "Completed successfully." : `Finished with status: ${status}`);
  void checkForResumableRun();
}

// ── Stream rendering ──────────────────────────────────────────────────────────

function agentDisplayName(source) {
  const names = {
    backendDeveloper: "Backend Developer",
    "backend-developer": "Backend Developer",
    BackendDeveloper: "Backend Developer",
    frontendDeveloper: "Frontend Developer",
    "frontend-developer": "Frontend Developer",
    FrontendDeveloper: "Frontend Developer",
    codingStyle: "Coding Style",
    "coding-style": "Coding Style",
    CodingStyle: "Coding Style",
    security: "Security",
    Security: "Security",
    architecture: "Architecture",
    Architecture: "Architecture",
    orchestration: "Orchestration",
    Orchestration: "Orchestration",
    planning: "Planning",
    Planning: "Planning",
    build: "Build",
    Build: "Build"
  };
  return names[source] || source;
}

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

function ensureStreamSection(sectionId, agentRole, title) {
  if (!streamSections[sectionId]) {
    streamSections[sectionId] = {
      agentId: sectionId,
      agentRole,
      title: title || agentRole,
      segments: [],
      updatedAt: null,
      segmentCount: 0,
      renderHandle: null,
      renderVersion: 0
    };
    streamOrder.push(sectionId);
  }

  const section = streamSections[sectionId];
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

function recordStreamEvent(entry) {
  const agentId = readEventField(entry, "agentId");
  if (!agentId) return;

  const agentRole = readEventField(entry, "agentRole") || readEventField(entry, "source") || "unknown";
  const message = readEventField(entry, "message") || "";
  if (!message) return;

  const streamKind = readEventField(entry, "streamKind") || "assistant";
  const isNewTurn = streamKind === "prompt";
  const sectionId = resolveSectionId(agentId, isNewTurn);
  const title = streamKind === "tool-call" || streamKind === "prompt" || streamKind === "reasoning" ? null : readEventField(entry, "title");
  const section = ensureStreamSection(sectionId, agentRole, title);

  if (section.segmentCount === 0) {
    hideAgentSpinningUp(agentRole);
  }
  streamEmptyEl.classList.add("hidden");

  if (streamKind === "tool-call") {
    const group = getOrCreateToolGroup(section);
    group.calls.push(message);
    section.segmentCount += 1;
    section.updatedAt = readEventField(entry, "timestampUtc") || new Date().toISOString();
    scheduleIncrementalRender(sectionId);
    return;
  }

  if (streamKind === "prompt") {
    createPromptSegment(section, message);
    section.segmentCount += 1;
    section.updatedAt = readEventField(entry, "timestampUtc") || new Date().toISOString();
    scheduleIncrementalRender(sectionId);
    return;
  }

  if (streamKind === "reasoning") {
    const seg = getOrCreateReasoningSegment(section);
    seg.content += message;
    section.segmentCount += 1;
    section.updatedAt = readEventField(entry, "timestampUtc") || new Date().toISOString();
    scheduleIncrementalRender(sectionId);
    return;
  }

  const seg = getOrCreateTextSegment(section);
  seg.content += message;
  section.segmentCount += 1;
  section.updatedAt = readEventField(entry, "timestampUtc") || new Date().toISOString();

  scheduleIncrementalRender(sectionId);
}

let _pendingRenderIds = new Set();
let _pendingRenderFrame = null;

function flushPendingRenders() {
  _pendingRenderFrame = null;
  const ids = _pendingRenderIds;
  _pendingRenderIds = new Set();

  let needsFullRender = false;
  for (const sectionId of ids) {
    const section = streamSections[sectionId];
    if (!section) continue;
    const container = streamSectionsEl.querySelector(`[data-agent-id="${CSS.escape(sectionId)}"]`);
    if (!container) {
      needsFullRender = true;
      break;
    }
    const openState = captureDetailsState(container);
    setSanitizedHtml(container, buildSectionBodyHtml(section));
    restoreDetailsState(container, openState);
  }

  if (needsFullRender) renderAllSections();

  if (streamAutoScroll) {
    streamSectionsEl.scrollTop = streamSectionsEl.scrollHeight;
  }

  for (const sectionId of ids) {
    scheduleMarkdownRender(sectionId);
  }
}

function scheduleIncrementalRender(sectionId) {
  _pendingRenderIds.add(sectionId);
  if (!_pendingRenderFrame) {
    _pendingRenderFrame = (globalThis.requestAnimationFrame || globalThis.setTimeout)(flushPendingRenders, 16);
  }
}

function scheduleMarkdownRender(sectionId) {
  const section = streamSections[sectionId];
  if (!section) return;
  if (section.renderHandle) clearTimeout(section.renderHandle);
  section.renderHandle = setTimeout(() => {
    section.renderHandle = null;
    void renderSectionMarkdown(sectionId);
  }, STREAM_RENDER_DELAY_MS);
}

async function renderSectionMarkdown(sectionId) {
  const section = streamSections[sectionId];
  if (!section) return;

  const version = ++section.renderVersion;
  const textSegments = section.segments.filter(s => (s.type === "text" || s.type === "prompt" || s.type === "reasoning") && s.content);

  const renderedSegments = await Promise.all(textSegments.map(async seg => {
    try {
      const result = await requestJson("/api/markdown/render", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ markdown: seg.content })
      });
      return result?.html || `<pre>${escapeHtml(seg.content)}</pre>`;
    } catch {
      return `<pre>${escapeHtml(seg.content)}</pre>`;
    }
  }));

  if (streamSections[sectionId]?.renderVersion !== version) return;

  textSegments.forEach((seg, index) => {
    seg.html = renderedSegments[index];
  });

  const container = streamSectionsEl.querySelector(`[data-agent-id="${CSS.escape(sectionId)}"]`);
  if (container) {
    const openState = captureDetailsState(container);
    setSanitizedHtml(container, buildSectionBodyHtml(section));
    restoreDetailsState(container, openState);
    if (streamAutoScroll) streamSectionsEl.scrollTop = streamSectionsEl.scrollHeight;
  } else {
    renderAllSections();
  }
}

function renderAllSections() {
  streamSectionsEl.replaceChildren();
  const hasSections = streamOrder.length > 0;
  streamEmptyEl.classList.toggle("hidden", hasSections);
  if (hasSections) hideStreamStarting();

  streamOrder.forEach(sectionId => {
    const section = streamSections[sectionId];
    if (!section) return;

    const details = document.createElement("details");
    details.className = "stream-section";
    details.open = true;

    const summary = document.createElement("summary");
    summary.className = "stream-section-summary";

    const summaryLeft = document.createElement("div");
    const summaryTitle = document.createElement("strong");
    summaryTitle.textContent = section.title || section.agentRole;
    const summaryRole = document.createElement("span");
    summaryRole.textContent = agentDisplayName(section.agentRole);
    summaryLeft.append(summaryTitle, summaryRole);

    const summaryMeta = document.createElement("div");
    summaryMeta.className = "stream-section-meta";
    const summaryTime = document.createElement("span");
    summaryTime.textContent = section.updatedAt
      ? new Date(section.updatedAt).toLocaleString([], { hour: "2-digit", minute: "2-digit" })
      : "Pending";
    summaryMeta.append(summaryTime);

    summary.append(summaryLeft, summaryMeta);

    const body = document.createElement("div");
    body.className = "markdown-surface stream-markdown";
    body.dataset.agentId = sectionId;
    setSanitizedHtml(body, buildSectionBodyHtml(section));
    details.append(summary, body);
    streamSectionsEl.append(details);
  });

  if (streamAutoScroll) streamSectionsEl.scrollTop = streamSectionsEl.scrollHeight;
}

function showStreamStarting() {
  const existing = streamSectionsEl.querySelector("#wikidoc-stream-starting");
  if (existing) return;
  const el = document.createElement("div");
  el.id = "wikidoc-stream-starting";
  el.className = "stream-starting";
  el.textContent = "Starting";
  streamSectionsEl.append(el);
}

function hideStreamStarting() {
  streamSectionsEl.querySelector("#wikidoc-stream-starting")?.remove();
}

function showStreamCompleted() {
  hideStreamStarting();
  const existing = streamSectionsEl.querySelector("#wikidoc-stream-completed");
  if (existing) return;
  const el = document.createElement("div");
  el.id = "wikidoc-stream-completed";
  el.className = "stream-completed";
  el.textContent = "Completed";
  streamSectionsEl.append(el);
  if (streamAutoScroll) streamSectionsEl.scrollTop = streamSectionsEl.scrollHeight;
}

const _spinningUpEls = {};

function showAgentSpinningUp(source) {
  const key = source.toLowerCase();
  if (_spinningUpEls[key]?.parentNode) return;
  const el = document.createElement("div");
  el.className = "stream-starting stream-agent-spinning-up";
  el.dataset.spinningKey = key;
  el.textContent = `Spinning up ${agentDisplayName(source)}`;
  _spinningUpEls[key] = el;
  streamSectionsEl.append(el);
  if (streamAutoScroll) streamSectionsEl.scrollTop = streamSectionsEl.scrollHeight;
}

function hideAgentSpinningUp(source) {
  const key = source.toLowerCase();
  const el = _spinningUpEls[key];
  if (el) {
    el.remove();
    delete _spinningUpEls[key];
  }
}

function resetStream() {
  closeEventStream();
  Object.values(streamSections).forEach(s => {
    if (s.renderHandle) clearTimeout(s.renderHandle);
  });
  if (_pendingRenderFrame) {
    (globalThis.cancelAnimationFrame || clearTimeout)(_pendingRenderFrame);
    _pendingRenderFrame = null;
    _pendingRenderIds.clear();
  }
  streamSections = {};
  streamOrder = [];
  Object.keys(_agentTurnCounters).forEach(k => delete _agentTurnCounters[k]);
  Object.keys(_agentCurrentSectionId).forEach(k => delete _agentCurrentSectionId[k]);
  Object.keys(_spinningUpEls).forEach(key => {
    _spinningUpEls[key]?.remove();
    delete _spinningUpEls[key];
  });
  streamSectionsEl.replaceChildren();
  streamEmptyEl.classList.remove("hidden");
  streamAutoScroll = true;
  resetProgressTracker();
}

// ── Progress tracker ──────────────────────────────────────────────────────────

function handleWikiDocProgress(message) {
  // Format: wikidoc:<action>:<detail>:<done>/<total>
  const parts = message.split(":");
  if (parts.length < 4) return;

  const action = parts[1];
  const detail = parts[2];
  const fraction = parts[3];
  const [doneStr, totalStr] = fraction.split("/");
  const done = parseInt(doneStr, 10);
  const total = parseInt(totalStr, 10);

  if (!isNaN(total) && total > 0) progressTotal = total;
  if (!isNaN(done)) progressDone = done;

  switch (action) {
    case "progress":
      showProgressPanel();
      updateProgressBar();
      break;
    case "repo-started":
      activeAgents.set(detail, Date.now());
      showProgressPanel();
      updateProgressBar();
      renderActiveAgents();
      break;
    case "repo-completed":
      activeAgents.delete(detail);
      updateProgressBar();
      renderActiveAgents();
      break;
    case "megawiki-started":
      activeAgents.clear();
      activeAgents.set("Megawiki synthesis", Date.now());
      updateProgressBar();
      renderActiveAgents(true);
      break;
    case "megawiki-completed":
      activeAgents.clear();
      updateProgressBar();
      renderActiveAgents();
      break;
  }
}

function showProgressPanel() {
  progressPanel.classList.remove("hidden");
}

function hideProgressPanel() {
  progressPanel.classList.add("hidden");
}

function updateProgressBar() {
  if (progressTotal <= 0) return;
  const pct = Math.min(100, Math.round((progressDone / progressTotal) * 100));
  progressBar.style.width = `${pct}%`;
  progressCounter.textContent = `${progressDone} / ${progressTotal} repositories`;
}

function renderActiveAgents(isMegawiki = false) {
  activeAgentsEl.replaceChildren();
  for (const [name] of activeAgents) {
    const slot = document.createElement("span");
    slot.className = "wikidoc-agent-slot";

    const dot = document.createElement("span");
    dot.className = "wikidoc-agent-slot-dot" + (isMegawiki ? " megawiki" : "");

    const label = document.createElement("span");
    label.textContent = name;

    slot.append(dot, label);
    activeAgentsEl.append(slot);
  }
}

function resetProgressTracker() {
  progressTotal = 0;
  progressDone = 0;
  activeAgents.clear();
  progressBar.style.width = "0%";
  progressCounter.textContent = "";
  activeAgentsEl.replaceChildren();
  hideProgressPanel();
}

// ── Status display ─────────────────────────────────────────────────────────────

function setStatus(message, level = "info") {
  statusEl.textContent = message;
  statusEl.dataset.level = level;
}

// ── Desktop chrome ─────────────────────────────────────────────────────────────

function applyWikiDocDesktopChrome() {
  const bridge = globalThis.archHarnessDesktop || null;
  if (!bridge?.chrome) return;
  const root = document.documentElement;
  root.dataset.desktopPlatform = bridge.chrome.platform;
  if (bridge.chrome.titleBarOverlay) {
    root.dataset.titleBarOverlay = "true";
  }
}

// ── Init ───────────────────────────────────────────────────────────────────────

streamSectionsEl.addEventListener("scroll", () => {
  const el = streamSectionsEl;
  streamAutoScroll = el.scrollHeight - el.scrollTop - el.clientHeight < 40;
});

generateButton.addEventListener("click", () => {
  if (!isRunning) {
    void startWikiDocRun().catch(error => {
      console.error("Wiki doc run failed:", error);
      generateButton.disabled = false;
      generateButton.textContent = "Generate";
      setStatus(`Error: ${error.message || "Unknown error"}`, "error");
    });
  }
});

resumeButton.addEventListener("click", () => {
  if (!isRunning) {
    void resumeWikiDocRun().catch(error => {
      console.error("Wiki doc resume failed:", error);
      resumeButton.disabled = false;
      resumeButton.textContent = "Resume";
      generateButton.disabled = false;
      setStatus(`Error: ${error.message || "Unknown error"}`, "error");
    });
  }
});

browseButton.addEventListener("click", () => {
  void browseScanRoot();
});

let _resumeCheckHandle = null;
function scheduleResumeCheck() {
  if (_resumeCheckHandle) clearTimeout(_resumeCheckHandle);
  _resumeCheckHandle = setTimeout(() => {
    _resumeCheckHandle = null;
    void checkForResumableRun();
  }, 400);
}

folderInput.addEventListener("input", scheduleResumeCheck);
folderInput.addEventListener("change", scheduleResumeCheck);

if (!desktopBridge?.selectFolder) {
  browseButton.classList.add("hidden");
}

applyWikiDocDesktopChrome();
