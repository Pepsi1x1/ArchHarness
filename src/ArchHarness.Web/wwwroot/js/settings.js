import { ROLE_LABELS } from './constants.js';
import { state, elements } from './state.js';
import { requestJson } from './api.js';
import { setSelectValue } from './utils.js';
import { closeModal } from './modals.js';
import { createDropdown, updateDropdown, createDropdownRegistry } from './dropdown.js';

const settingsRegistry = createDropdownRegistry();
let permissionDropdown = null;

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
  if (permissionDropdown) {
    updateDropdown(permissionDropdown, getPermissionOptions(), state.settings.defaults.permissionHandlerMode || "");
  }
  setSelectValue(elements.runMode, state.settings.defaults.architectureReviewMode ? "architecture-review" : "standard");
  elements.settingsArchitectureMode.checked = !!state.settings.defaults.architectureReviewMode;
  elements.settingsArchitecturePrompt.value = state.settings.defaults.architectureReviewPrompt || "";
  elements.settingsWikidocParallelism.value = state.settings.defaults.wikidocParallelism ?? 4;
}

function getPermissionOptions() {
  const modes = state.bootstrap?.permissionModes || [];
  return modes.map(m => ({ value: m, label: m }));
}

export function populateSettingsPermissionMode() {
  const options = getPermissionOptions();
  const current = state.settings?.defaults?.permissionHandlerMode || "";
  if (permissionDropdown) {
    updateDropdown(permissionDropdown, options, current);
    return;
  }

  permissionDropdown = createDropdown("settings-permission-mode", options, current, {
    onSelect: () => {},
    registry: settingsRegistry,
    extraClass: "settings-dropdown"
  });
  elements.settingsPermissionModeWrap.replaceChildren(permissionDropdown);
}

export function renderSettingsForm() {
  if (!state.settings) {
    return;
  }

  elements.settingsGrid.replaceChildren();
  Object.entries(ROLE_LABELS).forEach(([key, label]) => {
    const modelOptions = state.models.map(model => ({
      value: model.modelId,
      label: model.costBand ? `${model.displayName} • ${model.costBand}` : model.displayName
    }));

    const hasReasoning = key === "planning" || key === "wikidoc";

    const modelDropdown = createDropdown(
      `settings-model-${key}`,
      modelOptions,
      state.settings.agentModels?.[key] || "",
      {
        onSelect: (value) => {
          if (hasReasoning) {
            const rWrap = document.querySelector(`[data-dropdown-id="settings-reasoning-${key}"]`);
            if (rWrap) {
              const newOpts = getReasoningOptions(value);
              updateDropdown(rWrap, newOpts, rWrap.dataset.value || "");
            }
          }
        },
        registry: settingsRegistry,
        extraClass: "settings-dropdown"
      }
    );

    const agentLabel = document.createElement("span");
    agentLabel.className = "settings-grid-label";
    agentLabel.textContent = label;

    const modelCell = document.createElement("div");
    modelCell.append(modelDropdown);

    const rLabelEl = document.createElement("span");
    const rCell = document.createElement("div");

    if (hasReasoning) {
      const reasoningOpts = getReasoningOptions(state.settings.agentModels?.[key] || "");
      const currentReasoning = state.settings.agentReasoningEfforts?.[key] || "";
      const reasoningDropdown = createDropdown(
        `settings-reasoning-${key}`,
        reasoningOpts,
        reasoningOpts.some(o => o.value === currentReasoning) ? currentReasoning : "",
        { onSelect: () => {}, registry: settingsRegistry, extraClass: "settings-dropdown" }
      );
      rLabelEl.className = "settings-grid-label settings-grid-label--dim";
      rLabelEl.textContent = "Reasoning";
      rCell.append(reasoningDropdown);
    } else {
      rLabelEl.className = "settings-grid-empty";
      rCell.className = "settings-grid-empty";
    }

    elements.settingsGrid.append(agentLabel, modelCell, rLabelEl, rCell);
  });
}

function getReasoningOptions(modelId) {
  const model = state.models.find(m => m.modelId === modelId);
  const supported = Array.isArray(model?.supportedReasoningEfforts) ? model.supportedReasoningEfforts : [];
  if (!supported.length) {
    return [{ value: "", label: "Reasoning not supported", disabled: true }];
  }
  const defaultLabel = model.defaultReasoningEffort
    ? `Model default (${model.defaultReasoningEffort})`
    : "Model default";
  return [
    { value: "", label: defaultLabel },
    ...supported.map(e => ({ value: e, label: e.toUpperCase() }))
  ];
}

export function closeSettingsDropdowns() {
  settingsRegistry.close();
}

function collectSettingsPayload() {
  const agentModels = {};
  Object.keys(ROLE_LABELS).forEach(key => {
    const wrap = document.querySelector(`[data-dropdown-id="settings-model-${key}"]`);
    agentModels[key] = wrap?.dataset.value || null;
  });

  const planningWrap = document.querySelector('[data-dropdown-id="settings-reasoning-planning"]');
  const wikidocWrap = document.querySelector('[data-dropdown-id="settings-reasoning-wikidoc"]');

  return {
    agentModels,
    agentReasoningEfforts: {
      planning: planningWrap?.dataset.value || null,
      wikidoc: wikidocWrap?.dataset.value || null
    },
    defaults: {
      permissionHandlerMode: permissionDropdown?.dataset.value || "",
      architectureReviewMode: elements.settingsArchitectureMode.checked,
      architectureReviewPrompt: elements.settingsArchitecturePrompt.value.trim() || null,
      wikidocParallelism: Number.parseInt(elements.settingsWikidocParallelism.value, 10) || 4
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
