import { IDLE_INTERACTION_POLL_MS, ACTIVE_INTERACTION_POLL_MS, WORKFLOWS } from './constants.js';
import { state, elements } from './state.js';
import { requestJson } from './api.js';
import { isPlanningModeEnabled } from './composer.js';
import { renderTopbar } from './projects.js';

function getPendingInteractionSignature(pending) {
  if (!pending) {
    return null;
  }

  return JSON.stringify({
    kind: pending.kind || "",
    question: pending.question || "",
    choices: Array.isArray(pending.choices) ? pending.choices : [],
    questions: Array.isArray(pending.questions) ? pending.questions : [],
    permissionKind: pending.permissionKind || "",
    sessionId: pending.sessionId || "",
    toolName: pending.toolName || "",
    specMarkdown: pending.specMarkdown || "",
    planSummary: pending.planSummary || "",
    planReviewMarkdown: pending.planReviewMarkdown || "",
    runId: pending.runId || ""
  });
}

function dismissPendingInteraction() {
  const signature = state.pendingInteractionSignature || getPendingInteractionSignature(state.pendingInteraction);
  state.dismissedPendingInteractionSignature = signature;
  state.pendingInteraction = null;
  state.pendingInteractionSignature = null;
  state.pendingInteractionDraft = "";
  state.pendingInteractionDrafts = {};
  state.pendingPlanRevisionDraft = "";
}

function setPendingInteraction(pending) {
  const nextSignature = getPendingInteractionSignature(pending);

  if (nextSignature && nextSignature === state.dismissedPendingInteractionSignature) {
    pending = null;
  } else if (!nextSignature || nextSignature !== state.dismissedPendingInteractionSignature) {
    state.dismissedPendingInteractionSignature = null;
  }

  const normalizedSignature = getPendingInteractionSignature(pending);
  const changed = normalizedSignature !== state.pendingInteractionSignature;

  state.pendingInteraction = pending;
  if (changed) {
    state.pendingInteractionSignature = normalizedSignature;
    state.pendingInteractionDraft = "";
    state.pendingInteractionDrafts = {};
    state.pendingPlanRevisionDraft = "";
  }

  return changed;
}

export function renderInlineInteraction() {
  const pending = state.pendingInteraction;
  clearPlanApprovalChatControls();
  if (!pending) {
    elements.inlineInteraction.classList.add("hidden");
    elements.inlineInteraction.replaceChildren();
    state.pendingInteractionSignature = null;
    state.pendingInteractionDraft = "";
    state.pendingInteractionDrafts = {};
    state.pendingPlanRevisionDraft = "";
    renderTopbar();
    return;
  }

  elements.inlineInteraction.classList.remove("hidden");
  elements.inlineInteraction.replaceChildren();

  const hasQuestionBatch = pending.kind === "user-input"
    && Array.isArray(pending.questions)
    && pending.questions.length > 0;
  const hasFreeTextInput = pending.kind === "user-input" && !hasQuestionBatch;

  elements.inlineInteraction.classList.toggle("plan-approval", pending.kind === "plan-approval");
  elements.inlineInteraction.classList.toggle("question-batch", hasQuestionBatch);
  elements.inlineInteraction.classList.toggle("single-input", hasFreeTextInput);

  if (pending.kind === "plan-approval") {
    renderPlanApprovalInteraction(pending);
    renderTopbar();
    return;
  }

  const label = document.createElement("div");
  label.className = "inline-interaction-copy";
  const labelTitle = document.createElement("strong");
  const isPlanningQuestion = pending.kind === "user-input"
    && (state.selectedRunState?.workflow === WORKFLOWS.PLANNING || isPlanningModeEnabled());
  labelTitle.textContent = getLabelTitle(pending.kind, isPlanningQuestion, hasQuestionBatch);
  const labelQuestion = document.createElement("p");
  labelQuestion.textContent = pending.question || "";
  label.append(labelTitle, labelQuestion);
  elements.inlineInteraction.append(label);

  if (pending.choices?.length && pending.kind !== "plan-approval" && !hasQuestionBatch) {
    const row = document.createElement("div");
    row.className = "choice-row";
    pending.choices.forEach(choice => {
      const button = document.createElement("button");
      button.type = "button";
      button.className = "choice-chip";
      button.textContent = choice;
      button.addEventListener("click", () => submitUserInput(choice));
      row.append(button);
    });
    elements.inlineInteraction.append(row);
  }

  if (pending.kind === "permission") {
    renderPermissionInteraction();
  } else if (hasQuestionBatch) {
    renderQuestionBatchInteraction(pending);
  } else {
    renderFreeTextInteraction();
  }

  renderTopbar();
}

function getLabelTitle(kind, isPlanningQuestion, hasQuestionBatch) {
  if (kind === "permission") return "Permission";
  if (kind === "plan-approval") return "Planning";
  if (isPlanningQuestion) return hasQuestionBatch ? "Planning Questions" : "Planning Question";
  return hasQuestionBatch ? "Questions" : "Input";
}

function renderPermissionInteraction() {
  const actions = document.createElement("div");
  actions.className = "button-row";
  actions.append(
    interactionAction("Approve", "primary", () => submitPermission(true)),
    interactionAction("Deny", "danger", () => submitPermission(false))
  );
  elements.inlineInteraction.append(actions);
}

function renderPlanApprovalInteraction(pending) {
  if (renderPlanApprovalChatControls(pending)) {
    elements.inlineInteraction.classList.add("hidden");
    elements.inlineInteraction.replaceChildren();
    return;
  }

  const actions = document.createElement("div");
  actions.className = "button-row plan-approval-actions";
  actions.append(
    interactionAction("Approve", "primary", () => submitPlanApproval("approved", null)),
    interactionAction("Cancel", "danger", () => submitPlanApproval("canceled"))
  );
  elements.inlineInteraction.append(actions);
}

function renderPlanApprovalChatControls(pending) {
  const runId = pending.runId || state.activeRunId;
  if (!runId) {
    return false;
  }

  const planSurface = findLatestPlanReviewSurface(runId);
  if (!planSurface) {
    return false;
  }

  const controls = document.createElement("section");
  controls.className = "stream-plan-approval-actions";
  controls.dataset.planApprovalControls = "true";

  const actions = document.createElement("div");
  actions.className = "button-row plan-approval-actions";
  actions.append(
    interactionAction("Approve", "primary", () => submitPlanApproval("approved", null)),
    interactionAction("Cancel", "danger", () => submitPlanApproval("canceled"))
  );

  controls.append(actions);
  planSurface.append(controls);
  planSurface.closest("details")?.scrollIntoView({ block: "end", behavior: "smooth" });
  return true;
}

function findLatestPlanReviewSurface(runId) {
  const baseId = `planning-review-${runId}`;
  const baseDashId = `${baseId}-`;
  const baseHashId = `${baseId}#`;
  const selector = [
    `[data-agent-id="${CSS.escape(baseId)}"]`,
    `[data-agent-id^="${CSS.escape(baseDashId)}"]`,
    `[data-agent-id^="${CSS.escape(baseHashId)}"]`
  ].join(", ");
  const surfaces = Array.from(document.querySelectorAll(selector));
  return surfaces.at(-1) || null;
}

function clearPlanApprovalChatControls() {
  document.querySelectorAll("[data-plan-approval-controls='true']").forEach(element => element.remove());
}

function renderQuestionBatchInteraction(pending) {
  const scrollRegion = document.createElement("div");
  scrollRegion.className = "inline-interaction-scroll";

  const questionList = document.createElement("div");
  questionList.className = "inline-interaction-question-list";
  pending.questions.forEach((question, index) => {
    const field = document.createElement("label");
    field.className = "inline-interaction-field";
    const title = document.createElement("span");
    title.textContent = `Question ${index + 1}`;
    const copy = document.createElement("p");
    copy.className = "inline-interaction-field-copy";
    copy.textContent = question;
    const input = document.createElement("textarea");
    input.rows = 3;
    input.placeholder = "Type your response";
    const draftKey = String(index);
    input.value = state.pendingInteractionDrafts[draftKey] || "";
    input.addEventListener("input", () => {
      state.pendingInteractionDrafts[draftKey] = input.value;
    });
    field.append(title, copy, input);
    questionList.append(field);
  });
  scrollRegion.append(questionList);

  const actions = document.createElement("div");
  actions.className = "button-row";
  actions.append(interactionAction("Submit", "primary", () => submitUserInputs(
    pending.questions.map((_, index) => state.pendingInteractionDrafts[String(index)] || "")
  )));
  elements.inlineInteraction.append(scrollRegion, actions);
}

function renderFreeTextInteraction() {
  const field = document.createElement("label");
  field.className = "inline-interaction-field inline-interaction-free-text";
  const input = document.createElement("textarea");
  input.rows = 3;
  input.placeholder = "Type your response";
  input.value = state.pendingInteractionDraft;
  input.addEventListener("input", () => {
    state.pendingInteractionDraft = input.value;
  });
  field.append(input);

  const actions = document.createElement("div");
  actions.className = "button-row";
  actions.append(interactionAction("Submit", "primary", () => submitUserInput(input.value)));
  elements.inlineInteraction.append(field, actions);
}

function interactionAction(label, tone, onClick) {
  const button = document.createElement("button");
  button.type = "button";
  button.className = `interaction-action ${tone}`;
  button.textContent = label;
  button.addEventListener("click", onClick);
  return button;
}

export async function pollPendingInteraction() {
  if (state.pendingInteractionInFlight || state.isUnloading || document.hidden) {
    schedulePendingInteractionPoll(IDLE_INTERACTION_POLL_MS);
    return;
  }

  state.pendingInteractionInFlight = true;
  const controller = new AbortController();
  state.pendingInteractionAbortController = controller;
  let shouldRenderInteraction = false;

  try {
    shouldRenderInteraction = setPendingInteraction(await requestJson("/api/interactions/pending", { signal: controller.signal }));
  } catch (error) {
    if (error?.name !== "AbortError") {
      shouldRenderInteraction = setPendingInteraction(null);
    }
  } finally {
    state.pendingInteractionAbortController = null;
    state.pendingInteractionInFlight = false;
    if (shouldRenderInteraction) {
      renderInlineInteraction();
    }
    schedulePendingInteractionPoll(state.pendingInteraction ? ACTIVE_INTERACTION_POLL_MS : IDLE_INTERACTION_POLL_MS);
  }
}

async function submitUserInput(answer) {
  clearPendingInteractionPoll();
  abortPendingInteractionPoll();
  const pendingSnapshot = state.pendingInteraction;
  const pendingInteractionDraft = state.pendingInteractionDraft;
  const pendingInteractionDrafts = { ...state.pendingInteractionDrafts };
  const pendingPlanRevisionDraft = state.pendingPlanRevisionDraft;
  dismissPendingInteraction();
  renderInlineInteraction();

  try {
    await requestJson("/api/interactions/user-input", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ answer })
    });
    await pollPendingInteraction();
  } catch (error) {
    if (isStaleInteractionConflict(error, "user-input")) {
      schedulePendingInteractionPoll(IDLE_INTERACTION_POLL_MS);
      return;
    }

    state.dismissedPendingInteractionSignature = null;
    setPendingInteraction(pendingSnapshot);
    state.pendingInteractionDraft = pendingInteractionDraft;
    state.pendingInteractionDrafts = pendingInteractionDrafts;
    state.pendingPlanRevisionDraft = pendingPlanRevisionDraft;
    renderInlineInteraction();
    throw error;
  }
}

async function submitUserInputs(answers) {
  clearPendingInteractionPoll();
  abortPendingInteractionPoll();
  const pendingSnapshot = state.pendingInteraction;
  const pendingInteractionDraft = state.pendingInteractionDraft;
  const pendingInteractionDrafts = { ...state.pendingInteractionDrafts };
  const pendingPlanRevisionDraft = state.pendingPlanRevisionDraft;
  dismissPendingInteraction();
  renderInlineInteraction();

  try {
    await requestJson("/api/interactions/user-input", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ answers })
    });
    await pollPendingInteraction();
  } catch (error) {
    if (isStaleInteractionConflict(error, "user-input")) {
      schedulePendingInteractionPoll(IDLE_INTERACTION_POLL_MS);
      return;
    }

    state.dismissedPendingInteractionSignature = null;
    setPendingInteraction(pendingSnapshot);
    state.pendingInteractionDraft = pendingInteractionDraft;
    state.pendingInteractionDrafts = pendingInteractionDrafts;
    state.pendingPlanRevisionDraft = pendingPlanRevisionDraft;
    renderInlineInteraction();
    throw error;
  }
}

async function submitPermission(approved) {
  clearPendingInteractionPoll();
  abortPendingInteractionPoll();
  const pendingSnapshot = state.pendingInteraction;
  dismissPendingInteraction();
  renderInlineInteraction();

  try {
    await requestJson("/api/interactions/permission", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ approved })
    });
    await pollPendingInteraction();
  } catch (error) {
    if (isStaleInteractionConflict(error, "permission")) {
      schedulePendingInteractionPoll(IDLE_INTERACTION_POLL_MS);
      return;
    }

    state.dismissedPendingInteractionSignature = null;
    setPendingInteraction(pendingSnapshot);
    renderInlineInteraction();
    throw error;
  }
}

export async function submitPlanApproval(decision, reason, attachments = null) {
  clearPendingInteractionPoll();
  abortPendingInteractionPoll();
  const pendingSnapshot = state.pendingInteraction;
  const pendingInteractionDraft = state.pendingInteractionDraft;
  const pendingInteractionDrafts = { ...state.pendingInteractionDrafts };
  const pendingPlanRevisionDraft = state.pendingPlanRevisionDraft;
  dismissPendingInteraction();
  renderInlineInteraction();

  try {
    await requestJson("/api/interactions/plan-approval", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        decision,
        reason: reason || null,
        attachments: Array.isArray(attachments) && attachments.length > 0 ? attachments : null
      })
    });
    await pollPendingInteraction();
  } catch (error) {
    if (isStaleInteractionConflict(error, "plan-approval")) {
      schedulePendingInteractionPoll(IDLE_INTERACTION_POLL_MS);
      return;
    }

    state.dismissedPendingInteractionSignature = null;
    setPendingInteraction(pendingSnapshot);
    state.pendingInteractionDraft = pendingInteractionDraft;
    state.pendingInteractionDrafts = pendingInteractionDrafts;
    state.pendingPlanRevisionDraft = pendingPlanRevisionDraft;
    renderInlineInteraction();
    throw error;
  }
}

function isStaleInteractionConflict(error, kind) {
  return error?.status === 409
    && typeof error.message === "string"
    && error.message.includes(`No pending ${kind} request is active.`);
}

export function clearPendingInteractionPoll() {
  if (state.interactionPollHandle) {
    globalThis.clearTimeout(state.interactionPollHandle);
    state.interactionPollHandle = null;
  }
}

export function abortPendingInteractionPoll() {
  if (state.pendingInteractionAbortController) {
    state.pendingInteractionAbortController.abort();
    state.pendingInteractionAbortController = null;
  }
}

function schedulePendingInteractionPoll(delayMs) {
  clearPendingInteractionPoll();

  if (state.isUnloading || document.hidden) {
    return;
  }

  state.interactionPollHandle = globalThis.setTimeout(() => {
    state.interactionPollHandle = null;
    void pollPendingInteraction();
  }, delayMs);
}

export function handleVisibilityChange() {
  if (document.hidden) {
    clearPendingInteractionPoll();
    abortPendingInteractionPoll();
    return;
  }

  void pollPendingInteraction();
}
