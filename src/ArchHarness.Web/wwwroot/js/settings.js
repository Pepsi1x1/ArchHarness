import { ROLE_LABELS } from './constants.js';
import { state, elements } from './state.js';
import { requestJson } from './api.js';
import { populateSelect, setSelectValue } from './utils.js';
import { closeModal } from './modals.js';

export async function loadSettings() {
  state.settings = await requestJson("/api/settings");
  const modelsResponse = await requestJson("/api/models");
  state.models = modelsResponse?.models || [];
  renderSettingsForm();
  applySettingsDefaults();
}

export function applySettingsDefaults() {
  if (!state.settings) {
    return;
  }

  setSelectValue(elements.permissionMode, state.settings.defaults.permissionHandlerMode);
  setSelectValue(elements.settingsPermissionMode, state.settings.defaults.permissionHandlerMode);
  setSelectValue(elements.runMode, state.settings.defaults.architectureReviewMode ? "architecture-review" : "standard");
  elements.settingsArchitectureMode.checked = !!state.settings.defaults.architectureReviewMode;
  elements.settingsArchitecturePrompt.value = state.settings.defaults.architectureReviewPrompt || "";
}

export function renderSettingsForm() {
  if (!state.settings) {
    return;
  }

  elements.settingsGrid.replaceChildren();
  Object.entries(ROLE_LABELS).forEach(([key, label]) => {
    const wrapper = document.createElement("div");
    wrapper.className = "field settings-field";
    const title = document.createElement("span");
    title.textContent = label;

    const select = document.createElement("select");
    select.id = `settings-model-${key}`;
    state.models.forEach(model => {
      const option = document.createElement("option");
      option.value = model.modelId;
      option.textContent = model.costBand
        ? `${model.displayName} • ${model.costBand}`
        : model.displayName;
      select.append(option);
    });

    setSelectValue(select, state.settings.agentModels[key]);
    wrapper.append(title, select);

    if (key === "planning") {
      const reasoningTitle = document.createElement("span");
      reasoningTitle.textContent = "Planning Reasoning";

      const reasoningSelect = document.createElement("select");
      reasoningSelect.id = "settings-reasoning-planning";
      populatePlanningReasoningSelect(
        reasoningSelect,
        select.value,
        state.settings.agentReasoningEfforts?.planning || "");

      select.addEventListener("change", () => {
        populatePlanningReasoningSelect(reasoningSelect, select.value, reasoningSelect.value || "");
      });

      wrapper.append(reasoningTitle, reasoningSelect);
    }

    elements.settingsGrid.append(wrapper);
  });
}

function getModelMetadata(modelId) {
  return state.models.find(model => model.modelId === modelId) || null;
}

function populatePlanningReasoningSelect(select, modelId, selectedValue) {
  select.replaceChildren();

  const model = getModelMetadata(modelId);
  let supportedReasoningEfforts = [];
  if (Array.isArray(model?.supportedReasoningEfforts)) {
    supportedReasoningEfforts = model.supportedReasoningEfforts;
  }
  let defaultLabel = "Reasoning not supported";
  if (model?.defaultReasoningEffort) {
    defaultLabel = `Model default (${model.defaultReasoningEffort})`;
  } else if (supportedReasoningEfforts.length > 0) {
    defaultLabel = "Model default";
  }

  const defaultOption = document.createElement("option");
  defaultOption.value = "";
  defaultOption.textContent = defaultLabel;
  select.append(defaultOption);

  supportedReasoningEfforts.forEach(reasoningEffort => {
    const option = document.createElement("option");
    option.value = reasoningEffort;
    option.textContent = reasoningEffort.toUpperCase();
    select.append(option);
  });

  select.disabled = supportedReasoningEfforts.length === 0;
  if (select.disabled) {
    select.value = "";
    return;
  }

  setSelectValue(select, supportedReasoningEfforts.includes(selectedValue) ? selectedValue : "");
}

function collectSettingsPayload() {
  const agentModels = {};
  Object.keys(ROLE_LABELS).forEach(key => {
    agentModels[key] = document.getElementById(`settings-model-${key}`).value;
  });

  const planningReasoningSelect = document.getElementById("settings-reasoning-planning");

  return {
    agentModels,
    agentReasoningEfforts: {
      planning: planningReasoningSelect && !planningReasoningSelect.disabled
        ? planningReasoningSelect.value || null
        : null
    },
    defaults: {
      permissionHandlerMode: elements.settingsPermissionMode.value,
      architectureReviewMode: elements.settingsArchitectureMode.checked,
      architectureReviewPrompt: elements.settingsArchitecturePrompt.value.trim() || null
    }
  };
}

export async function saveSettings(event) {
  event.preventDefault();
  state.settings = await requestJson("/api/settings", {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(collectSettingsPayload())
  });
  applySettingsDefaults();
  closeModal();
}

export function switchSettingsTab(tabName) {
  document.querySelectorAll(".settings-tab").forEach(btn => {
    const isActive = btn.dataset.tab === tabName;
    btn.classList.toggle("active", isActive);
    btn.setAttribute("aria-selected", isActive ? "true" : "false");
    btn.setAttribute("tabindex", isActive ? "0" : "-1");
  });

  const agentPanel = document.getElementById("settings-tab-agent");
  const providersPanel = document.getElementById("settings-tab-providers");
  const showAgentPanel = tabName === "agent-settings";
  const showProvidersPanel = tabName === "source-control-providers";

  agentPanel.classList.toggle("hidden", !showAgentPanel);
  agentPanel.hidden = !showAgentPanel;
  providersPanel.classList.toggle("hidden", !showProvidersPanel);
  providersPanel.hidden = !showProvidersPanel;
}

export function handleSettingsTabKeydown(event) {
  const tabs = Array.from(document.querySelectorAll(".settings-tab"));
  const currentIndex = tabs.indexOf(event.currentTarget);
  if (currentIndex < 0) {
    return;
  }

  const nextIndex = (() => {
    switch (event.key) {
      case "ArrowRight":
      case "ArrowDown":
        return (currentIndex + 1) % tabs.length;
      case "ArrowLeft":
      case "ArrowUp":
        return (currentIndex - 1 + tabs.length) % tabs.length;
      case "Home":
        return 0;
      case "End":
        return tabs.length - 1;
      default:
        return null;
    }
  })();

  if (nextIndex === null) {
    return;
  }

  event.preventDefault();
  const nextTab = tabs[nextIndex];
  switchSettingsTab(nextTab.dataset.tab);
  nextTab.focus();
}
