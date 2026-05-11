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
  elements.inlineInteraction.classList.toggle("plan-approval", pending.kind === "plan-approval");
  const hasQuestionBatch = pending.kind === "user-input"
    && Array.isArray(pending.questions)
    && pending.questions.length > 0;

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
  } else if (pending.kind === "plan-approval") {
    renderPlanApprovalInteraction();
  } else if (hasQuestionBatch) {
    renderQuestionBatchInteraction(pending);
  } else {
    renderFreeTextInteraction();
  }

  renderTopbar();
}

function getLabelTitle(kind, isPlanningQuestion, hasQuestionBatch) {
  if (kind === "permission") return "Permission";
  if (kind === "plan-approval") return "Plan Approval";
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

function renderPlanApprovalInteraction() {
  const scroll = document.createElement("div");
  scroll.className = "inline-interaction-scroll";
  const planHint = document.createElement("p");
  planHint.className = "inline-interaction-field-copy";
  planHint.textContent = "Review the Planning message in the agent stream, then approve it or describe what should change.";
  scroll.append(planHint);
  const revisionField = document.createElement("label");
  revisionField.className = "inline-interaction-field inline-interaction-revision";
  const revisionTitle = document.createElement("span");
  revisionTitle.textContent = "Revision request";
  const revisionCopy = document.createElement("p");
  revisionCopy.className = "inline-interaction-field-copy";
  revisionCopy.textContent = "Describe specific changes or request a materially different plan.";
  const revisionInput = document.createElement("textarea");
  revisionInput.rows = 4;
  revisionInput.placeholder = "Examples: split backend and frontend work, add migration steps, or reduce scope to API only.";
  revisionInput.value = state.pendingPlanRevisionDraft;
  revisionInput.addEventListener("input", () => {
    state.pendingPlanRevisionDraft = revisionInput.value;
  });
  revisionField.append(revisionTitle, revisionCopy, revisionInput);
  scroll.append(revisionField);
  elements.inlineInteraction.append(scroll);
  const actions = document.createElement("div");
  actions.className = "button-row plan-approval-actions";
  actions.append(
    interactionAction("Approve", "primary", () => submitPlanApproval("approved", null)),
    interactionAction("Revise Plan", "secondary", () => submitPlanApproval("regenerate", state.pendingPlanRevisionDraft.trim() || null)),
    interactionAction("Cancel", "danger", () => submitPlanApproval("canceled"))
  );
  elements.inlineInteraction.append(actions);
}

function renderQuestionBatchInteraction(pending) {
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
  const actions = document.createElement("div");
  actions.className = "button-row";
  actions.append(interactionAction("Submit", "primary", () => submitUserInputs(
    pending.questions.map((_, index) => state.pendingInteractionDrafts[String(index)] || "")
  )));
  elements.inlineInteraction.append(questionList, actions);
}

function renderFreeTextInteraction() {
  const input = document.createElement("textarea");
  input.rows = 3;
  input.placeholder = "Type your response";
  input.value = state.pendingInteractionDraft;
  input.addEventListener("input", () => {
    state.pendingInteractionDraft = input.value;
  });
  const actions = document.createElement("div");
  actions.className = "button-row";
  actions.append(interactionAction("Submit", "primary", () => submitUserInput(input.value)));
  elements.inlineInteraction.append(input, actions);
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
    state.dismissedPendingInteractionSignature = null;
    setPendingInteraction(pendingSnapshot);
    renderInlineInteraction();
    throw error;
  }
}

async function submitPlanApproval(decision, reason) {
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
      body: JSON.stringify({ decision, reason: reason || null })
    });
    await pollPendingInteraction();
  } catch (error) {
    state.dismissedPendingInteractionSignature = null;
    setPendingInteraction(pendingSnapshot);
    state.pendingInteractionDraft = pendingInteractionDraft;
    state.pendingInteractionDrafts = pendingInteractionDrafts;
    state.pendingPlanRevisionDraft = pendingPlanRevisionDraft;
    renderInlineInteraction();
    throw error;
  }
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
