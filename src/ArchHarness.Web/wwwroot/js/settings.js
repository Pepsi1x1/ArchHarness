import { ROLE_LABELS } from './constants.js';
import { state, elements } from './state.js';
import { requestJson } from './api.js';
import { populateSelect, setSelectValue } from './utils.js';
import { closeModal } from './modals.js';

let settingsMenuOpen = null;

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
  elements.settingsWikidocParallelism.value = state.settings.defaults.wikidocParallelism ?? 4;
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

    const modelDropdown = createSettingsDropdown(
      `settings-model-${key}`,
      modelOptions,
      state.settings.agentModels?.[key] || "",
      (value) => {
        if (key === "planning" || key === "wikidoc") {
          const rWrap = document.querySelector(`[data-dropdown-id="settings-reasoning-${key}"]`);
          if (rWrap) {
            const newOpts = getReasoningOptions(value);
            updateSettingsDropdown(rWrap, newOpts, newOpts.length ? rWrap.dataset.value || "" : "");
          }
        }
      }
    );

    const wrapper = document.createElement("div");
    wrapper.className = "field settings-field";
    const title = document.createElement("span");
    title.textContent = label;
    wrapper.append(title, modelDropdown);
    elements.settingsGrid.append(wrapper);

    if (key === "planning" || key === "wikidoc") {
      const reasoningOpts = getReasoningOptions(state.settings.agentModels?.[key] || "");
      const currentReasoning = state.settings.agentReasoningEfforts?.[key] || "";
      const reasoningDropdown = createSettingsDropdown(
        `settings-reasoning-${key}`,
        reasoningOpts,
        reasoningOpts.find(o => o.value === currentReasoning) ? currentReasoning : "",
        () => {}
      );

      const reasoningWrapper = document.createElement("div");
      reasoningWrapper.className = "field settings-field";
      reasoningWrapper.style.gridColumn = key === "wikidoc" ? "2" : "1";
      const reasoningTitle = document.createElement("span");
      reasoningTitle.textContent = key === "wikidoc" ? "Wiki Docs Reasoning" : "Planning Reasoning";
      reasoningWrapper.append(reasoningTitle, reasoningDropdown);
      elements.settingsGrid.append(reasoningWrapper);
    }
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

function createSettingsDropdown(id, options, selectedValue, onSelect) {
  const wrap = document.createElement("div");
  wrap.className = "settings-dropdown composer-dropdown";
  wrap.dataset.dropdownId = id;
  wrap.dataset.value = selectedValue || "";

  const button = document.createElement("button");
  button.type = "button";
  button.className = "settings-dropdown-button composer-dropdown-button";
  button.setAttribute("aria-haspopup", "menu");
  button.setAttribute("aria-expanded", "false");

  const labelSpan = document.createElement("span");
  labelSpan.textContent = options.find(o => o.value === selectedValue)?.label || selectedValue || "";

  const chevron = document.createElement("i");
  chevron.className = "fa-solid fa-chevron-down";
  chevron.setAttribute("aria-hidden", "true");
  button.append(labelSpan, chevron);

  const menu = document.createElement("div");
  menu.className = "composer-dropdown-menu hidden";
  menu.setAttribute("role", "menu");

  function buildMenuItems(opts, val) {
    menu.replaceChildren();
    opts.forEach(opt => {
      const item = document.createElement("button");
      item.type = "button";
      item.className = "composer-dropdown-item";
      item.setAttribute("role", "menuitemradio");
      item.setAttribute("aria-checked", opt.value === val ? "true" : "false");
      item.classList.toggle("current", opt.value === val);
      item.textContent = opt.label;
      item.disabled = !!opt.disabled;
      item.addEventListener("click", e => {
        e.stopPropagation();
        wrap.dataset.value = opt.value;
        labelSpan.textContent = opt.label;
        menu.querySelectorAll(".composer-dropdown-item").forEach(i => {
          const active = i === item;
          i.classList.toggle("current", active);
          i.setAttribute("aria-checked", active ? "true" : "false");
        });
        onSelect(opt.value);
        closeSettingsDropdowns();
      });
      menu.append(item);
    });
  }

  buildMenuItems(options, selectedValue);
  wrap._buildMenuItems = buildMenuItems;

  const hasChoices = options.filter(o => !o.disabled).length > 0;
  button.disabled = !hasChoices;

  button.addEventListener("click", e => {
    e.stopPropagation();
    const isOpen = settingsMenuOpen === id;
    closeSettingsDropdowns();
    if (!isOpen && hasChoices) {
      settingsMenuOpen = id;
      wrap.classList.add("open");
      menu.classList.remove("hidden");
      button.setAttribute("aria-expanded", "true");
    }
  });

  wrap.append(button, menu);
  return wrap;
}

function updateSettingsDropdown(wrap, options, value) {
  wrap.dataset.value = value || "";
  const labelSpan = wrap.querySelector(".composer-dropdown-button span");
  const selectedOpt = options.find(o => o.value === value);
  if (labelSpan) labelSpan.textContent = selectedOpt?.label || value || "";
  if (wrap._buildMenuItems) wrap._buildMenuItems(options, value);
  const btn = wrap.querySelector(".settings-dropdown-button");
  if (btn) btn.disabled = options.filter(o => !o.disabled).length === 0;
}

export function closeSettingsDropdowns() {
  settingsMenuOpen = null;
  document.querySelectorAll(".settings-dropdown.open").forEach(el => {
    el.classList.remove("open");
    el.querySelector(".composer-dropdown-menu")?.classList.add("hidden");
    el.querySelector(".settings-dropdown-button")?.setAttribute("aria-expanded", "false");
  });
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
      permissionHandlerMode: elements.settingsPermissionMode.value,
      architectureReviewMode: elements.settingsArchitectureMode.checked,
      architectureReviewPrompt: elements.settingsArchitecturePrompt.value.trim() || null,
      wikidocParallelism: parseInt(elements.settingsWikidocParallelism.value, 10) || 4
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
