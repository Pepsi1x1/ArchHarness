import {
  PAT_STORAGE_MODE_PROTECTED, PAT_STORAGE_MODE_PLAINTEXT,
  GITHUB_AUTH_MODE_NONE, GITHUB_AUTH_MODE_PAT, GITHUB_AUTH_MODE_OAUTH,
  REVIEW_FILTER_MAX_LENGTH, REVIEW_PULL_REQUEST_ID_MAX_LENGTH
} from './constants.js';
import { state, elements } from './state.js';
import { requestJson } from './api.js';

export const PROVIDER_META = {
  0: {
    numericValue: 0,
    label: "Azure DevOps Server",
    badgeClass: "provider-badge-ado-server",
    radioValue: "ado-server",
    visibleFields: ["serverUrl", "organization", "personalAccessToken"],
    fieldLabels: {
      organization: "Organization / Collection"
    },
    formValueFields: {
      serverUrl: "serverUrl",
      organization: "organization"
    },
    patHint: "Requires Code (Read) permission.",
    orgRequiredMessage: "Organization is required."
  },
  1: {
    numericValue: 1,
    label: "Azure DevOps Services",
    badgeClass: "provider-badge-ado-services",
    radioValue: "ado-services",
    visibleFields: ["organization", "personalAccessToken"],
    fieldLabels: {
      organization: "Organization"
    },
    formValueFields: {
      organization: "organization"
    },
    patHint: "Requires Code (Read) permission.",
    orgRequiredMessage: "Organization is required."
  },
  2: {
    numericValue: 2,
    label: "GitHub",
    badgeClass: "provider-badge-github",
    radioValue: "github",
    visibleFields: ["organization", "gitHubOwnerType", "personalAccessToken"],
    fieldLabels: {
      organization: "Owner / Organization"
    },
    formValueFields: {
      organization: "organization",
      gitHubOwnerType: "gitHubOwnerType"
    },
    patHint: "OAuth is recommended to avoid GitHub rate limits. A PAT still works when you need manual auth.",
    orgRequiredMessage: "Owner or organization is required for GitHub."
  }
};

const RADIO_PROVIDER_MAP = Object.fromEntries(
  Object.values(PROVIDER_META).map(meta => [meta.radioValue, meta.numericValue])
);
const PROVIDER_ALLOWED_PROTOCOLS = new Set(["https:"]);
const PROVIDER_FORM_FIELDS = {
  serverUrl: {
    input: elements.providerServerUrl,
    wrapper: elements.providerServerUrlWrap
  },
  organization: {
    input: elements.providerOrg,
    wrapper: elements.providerOrgWrap,
    labelElement: elements.providerOrgLabel,
    defaultLabel: "Organization"
  },
  gitHubOwnerType: {
    input: elements.providerGitHubOwnerType,
    wrapper: elements.providerGitHubOwnerTypeWrap,
    defaultValue: "0"
  },
  personalAccessToken: {
    input: elements.providerPat,
    wrapper: elements.providerPatWrap
  }
};

export function normalizeProviderField(value) {
  return String(value ?? "")
    .replaceAll(/[\u0000-\u001F\u007F]+/g, " ")
    .trim();
}

function normalizeProviderToken(value) {
  return String(value ?? "").trim();
}

function looksLikeProviderUrl(value) {
  if (!value) {
    return false;
  }

  try {
    const parsed = new URL(value);
    return parsed.protocol === "http:" || parsed.protocol === "https:";
  } catch {
    return false;
  }
}

export function normalizeReviewLookupValue(value, maxLength = REVIEW_FILTER_MAX_LENGTH) {
  return String(value ?? "")
    .replaceAll(/[\u0000-\u001F\u007F]+/g, " ")
    .trim()
    .slice(0, maxLength);
}

export function normalizeReviewPullRequestId(value) {
  const normalized = normalizeReviewLookupValue(value, REVIEW_PULL_REQUEST_ID_MAX_LENGTH);
  return /^\d+$/.test(normalized) ? normalized : "";
}

function normalizeProviderSummary(provider) {
  if (!provider || typeof provider !== "object") {
    return null;
  }

  const providerType = Number(provider.providerType ?? provider.provider);
  if (!Number.isInteger(providerType) || !Object.hasOwn(PROVIDER_META, providerType)) {
    return null;
  }

  return {
    providerType,
    displayName: normalizeProviderField(provider.displayName) || null,
    serverUrl: normalizeProviderField(provider.serverUrl) || null,
    organization: normalizeProviderField(provider.organization) || null,
    gitHubOwnerType: Number.isInteger(provider.gitHubOwnerType)
      ? Number(provider.gitHubOwnerType)
      : 0,
    gitHubAuthenticationMode: Number.isInteger(provider.gitHubAuthenticationMode)
      ? Number(provider.gitHubAuthenticationMode)
      : GITHUB_AUTH_MODE_NONE,
    gitHubAuthenticatedUser: normalizeProviderField(provider.gitHubAuthenticatedUser) || null,
    personalAccessToken: null,
    hasStoredPersonalAccessToken: provider.hasStoredPersonalAccessToken === true,
    personalAccessTokenStorageMode: Number.isInteger(provider.personalAccessTokenStorageMode)
      ? Number(provider.personalAccessTokenStorageMode)
      : PAT_STORAGE_MODE_PROTECTED,
    isEnabled: provider.isEnabled !== false
  };
}

export function normalizeProviderCollection(result) {
  let providers = [];
  if (Array.isArray(result)) {
    providers = result;
  } else if (Array.isArray(result?.providers)) {
    providers = result.providers;
  }

  return providers
    .map(normalizeProviderSummary)
    .filter(provider => provider !== null);
}

function getSelectedProviderRadioValue() {
  return document.querySelector('input[name="pf-type"]:checked')?.value || null;
}

export function getProviderMetaByType(providerType) {
  if (providerType == null) {
    return null;
  }

  return PROVIDER_META[providerType] || null;
}

function getProviderMetaByRadioValue(radioValue) {
  return getProviderMetaByType(radioValue == null ? null : RADIO_PROVIDER_MAP[radioValue]);
}

export function setProviderStatus(message = "", tone = null) {
  elements.providerTestStatus.textContent = message;
  elements.providerTestStatus.className = tone
    ? `sc-status sc-status-${tone}`
    : "sc-status";
}

function setProviderPatMasked(masked) {
  elements.providerPat.type = masked ? "password" : "text";
  elements.providerPatToggleIcon.className = masked ? "fa-solid fa-eye" : "fa-solid fa-eye-slash";
  elements.providerPatToggle.setAttribute("aria-label", masked ? "Show token" : "Hide token");
  elements.providerPatToggle.setAttribute("aria-pressed", masked ? "false" : "true");
}

function buildProviderPatHint(providerMeta) {
  if (!providerMeta) {
    return "";
  }

  const baseHint = providerMeta.patHint || "Requires Code (Read) permission.";

  if (state.providerClearStoredToken) {
    return `${baseHint} The stored token will be cleared when you save.`;
  }

  if (!state.editingProviderName) {
    return baseHint;
  }

  if (providerMeta.numericValue === 2 && state.editingProviderHasStoredToken && state.editingProviderGitHubAuthenticationMode === GITHUB_AUTH_MODE_OAUTH) {
    return `${baseHint} Leave the PAT field blank to keep the stored OAuth token, or use Clear saved token to remove it.`;
  }

  if (providerMeta.numericValue === 2 && state.editingProviderHasStoredToken) {
    return `${baseHint} Leave blank to keep the current token, or use Clear saved token to remove it.`;
  }

  if (providerMeta.numericValue === 2) {
    return `${baseHint} Leave blank to save without a token.`;
  }

  if (state.editingProviderHasStoredToken) {
    return `${baseHint} Leave blank to keep the current token, or use Clear saved token to remove it.`;
  }

  return `${baseHint} Enter a token to save it with this provider.`;
}

function updateProviderClearTokenControls(providerMeta) {
  const showClearAction = Boolean(
    state.editingProviderName
    && state.editingProviderHasStoredToken
    && providerMeta?.visibleFields.includes("personalAccessToken")
  );

  elements.providerPatClear.classList.toggle("hidden", !showClearAction);

  if (!showClearAction) {
    elements.providerPatClearNote.classList.add("hidden");
    elements.providerPatClearNote.textContent = "";
    elements.providerPatClear.textContent = "Clear saved token";
    return;
  }

  elements.providerPatClear.textContent = state.providerClearStoredToken
    ? "Keep saved token"
    : "Clear saved token";

  if (state.providerClearStoredToken) {
    elements.providerPatClearNote.textContent = providerMeta?.numericValue === 2
      ? "The stored GitHub credential will be removed when you save. Enter a new token or authorize with GitHub to replace it."
      : "The stored token will be removed when you save. Enter a new token before saving if you want to replace it instead.";
    elements.providerPatClearNote.classList.remove("hidden");
    return;
  }

  elements.providerPatClearNote.classList.add("hidden");
  elements.providerPatClearNote.textContent = "";
}

function clearGitHubOAuthState() {
  if (state.providerGitHubOAuthPollHandle) {
    globalThis.clearTimeout(state.providerGitHubOAuthPollHandle);
  }

  state.providerGitHubOAuthFlow = null;
  state.providerGitHubOAuthToken = null;
  state.providerGitHubOAuthPollHandle = null;
  elements.providerGitHubOAuthLink.href = "#";
  elements.providerGitHubOAuthLink.classList.add("hidden");
  elements.providerGitHubOAuthCode.textContent = "";
  elements.providerGitHubOAuthCodeRow.classList.add("hidden");
  elements.providerGitHubOAuthCopyNote.classList.add("hidden");
  elements.providerGitHubOAuthCopy.disabled = false;
  elements.providerGitHubOAuthStart.disabled = false;
}

export async function copyGitHubOAuthCodeToClipboard() {
  const code = elements.providerGitHubOAuthCode.textContent?.trim() || "";
  if (!code) {
    return false;
  }

  try {
    await navigator.clipboard.writeText(code);
    elements.providerGitHubOAuthCopyNote.classList.remove("hidden");
    return true;
  } catch {
    return false;
  }
}

function getGitHubAuthModeLabel(provider) {
  if (provider.gitHubAuthenticationMode === GITHUB_AUTH_MODE_OAUTH) {
    return "Protected OAuth";
  }

  if (provider.gitHubAuthenticationMode === GITHUB_AUTH_MODE_PAT) {
    return "Protected PAT";
  }

  return "Unauthenticated";
}

function getProviderCredentialBadge(provider) {
  if (provider.providerType === 2) {
    let className = "provider-badge-protected";
    if (provider.gitHubAuthenticationMode === GITHUB_AUTH_MODE_NONE) {
      className = "provider-badge-neutral";
    }

    return {
      text: getGitHubAuthModeLabel(provider),
      className
    };
  }

  if (!provider.hasStoredPersonalAccessToken) {
    return {
      text: "No token",
      className: "provider-badge-neutral"
    };
  }

  return {
    text: "Protected PAT",
    className: "provider-badge-protected"
  };
}

function validateProviderServerUrl(serverUrl) {
  let parsedUrl;
  try {
    parsedUrl = new URL(serverUrl);
  } catch {
    return "Server URL must be an absolute HTTPS URL.";
  }

  if (!PROVIDER_ALLOWED_PROTOCOLS.has(parsedUrl.protocol)) {
    return "Server URL must use the https scheme.";
  }

  if (parsedUrl.username || parsedUrl.password) {
    return "Server URL cannot include embedded credentials.";
  }

  return null;
}

export async function loadProviders() {
  try {
    const result = await requestJson("/api/providers");
    state.providers = normalizeProviderCollection(result);
  } catch {
    state.providers = [];
  }
  renderProviderList();
}

export function renderProviderList() {
  elements.providerList.replaceChildren();

  if (!state.providers || state.providers.length === 0) {
    const empty = document.createElement("p");
    empty.className = "provider-list-empty";
    empty.textContent = "No providers configured.";
    elements.providerList.append(empty);
    return;
  }

  state.providers.forEach(provider => {
    const item = document.createElement("div");
    item.className = "provider-item";

    const info = document.createElement("div");
    info.className = "provider-item-info";

    const name = document.createElement("strong");
    name.className = "provider-item-name";
    name.textContent = provider.displayName || "Unnamed";

    const badge = document.createElement("span");
    const providerMeta = getProviderMetaByType(provider.providerType);
    badge.className = `provider-badge ${providerMeta?.badgeClass || ""}`;
    badge.textContent = providerMeta?.label || "Unknown";

    const storageBadge = document.createElement("span");
    const credentialBadge = getProviderCredentialBadge(provider);
    storageBadge.className = `provider-badge ${credentialBadge.className}`;
    storageBadge.textContent = credentialBadge.text;

    info.append(name, badge, storageBadge);

    if (provider.providerType === 2 && provider.gitHubAuthenticationMode === GITHUB_AUTH_MODE_OAUTH && provider.gitHubAuthenticatedUser) {
      const userBadge = document.createElement("span");
      userBadge.className = "provider-badge provider-badge-neutral";
      userBadge.textContent = `@${provider.gitHubAuthenticatedUser}`;
      info.append(userBadge);
    }

    const actions = document.createElement("div");
    actions.className = "provider-item-actions";

    const editBtn = document.createElement("button");
    editBtn.className = "ghost-button small-button";
    editBtn.type = "button";
    editBtn.textContent = "Edit";
    editBtn.addEventListener("click", () => openProviderSetup(provider));

    const deleteBtn = document.createElement("button");
    deleteBtn.className = "ghost-button small-button danger-button";
    deleteBtn.type = "button";
    deleteBtn.textContent = "Delete";
    deleteBtn.addEventListener("click", () => {
      void confirmDeleteProvider(provider.displayName).catch(err => console.error("Delete provider failed:", err));
    });

    actions.append(editBtn, deleteBtn);
    item.append(info, actions);
    elements.providerList.append(item);
  });
}

export function openProviderSetup(provider = null) {
  const normalizedProvider = normalizeProviderSummary(provider);
  const providerMeta = getProviderMetaByType(normalizedProvider?.providerType ?? null);
  state.editingProviderName = normalizedProvider?.displayName || null;
  state.editingProviderStorageMode = normalizedProvider?.personalAccessTokenStorageMode ?? PAT_STORAGE_MODE_PROTECTED;
  state.editingProviderHasStoredToken = normalizedProvider?.hasStoredPersonalAccessToken === true;
  state.editingProviderGitHubAuthenticationMode = normalizedProvider?.gitHubAuthenticationMode ?? GITHUB_AUTH_MODE_NONE;
  state.editingProviderGitHubAuthenticatedUser = normalizedProvider?.gitHubAuthenticatedUser || null;
  state.providerClearStoredToken = false;
  state.providerConnectionTested = false;

  elements.providerTypeRadios.forEach(radio => { radio.checked = false; });
  elements.providerDisplayName.value = "";
  clearGitHubOAuthState();
  Object.values(PROVIDER_FORM_FIELDS).forEach(field => {
    field.input.value = field.defaultValue || "";
    field.wrapper.classList.add("hidden");
    if (field.labelElement) {
      field.labelElement.textContent = field.defaultLabel || "";
    }
  });
  setProviderPatMasked(true);
  setProviderStatus();

  if (normalizedProvider) {
    const radioValue = providerMeta?.radioValue;
    const radio = radioValue
      ? document.querySelector(`input[name="pf-type"][value="${radioValue}"]`)
      : null;
    if (radio) {
      radio.checked = true;
      onProviderSetupTypeChange();
    }

    elements.providerDisplayName.value = normalizedProvider.displayName || "";

    Object.entries(providerMeta?.formValueFields || {}).forEach(([fieldKey, providerKey]) => {
      const field = PROVIDER_FORM_FIELDS[fieldKey];
      if (field) {
        field.input.value = normalizedProvider[providerKey] || "";
      }
    });
  }

  elements.providerList.classList.add("hidden");
  elements.btnAddProvider.classList.add("hidden");
  elements.providerSetup.classList.remove("hidden");
  elements.btnSaveProvider.textContent = provider ? "Update Provider" : "Save Provider";
  onProviderSetupTypeChange();
}

export function closeProviderSetup() {
  clearGitHubOAuthState();
  elements.providerSetup.classList.add("hidden");
  elements.providerList.classList.remove("hidden");
  elements.btnAddProvider.classList.remove("hidden");
  state.editingProviderName = null;
  state.editingProviderStorageMode = PAT_STORAGE_MODE_PROTECTED;
  state.editingProviderHasStoredToken = false;
  state.editingProviderGitHubAuthenticationMode = GITHUB_AUTH_MODE_NONE;
  state.editingProviderGitHubAuthenticatedUser = null;
  state.providerClearStoredToken = false;
  state.providerConnectionTested = false;
  setProviderPatMasked(true);
  setProviderStatus();
}

export function onProviderSetupTypeChange() {
  const meta = getProviderMetaByRadioValue(getSelectedProviderRadioValue());

  Object.entries(PROVIDER_FORM_FIELDS).forEach(([fieldKey, field]) => {
    const isVisible = meta?.visibleFields.includes(fieldKey) || false;
    field.wrapper.classList.toggle("hidden", !isVisible);
    if (field.labelElement) {
      field.labelElement.textContent = meta?.fieldLabels?.[fieldKey] || field.defaultLabel || "";
    }
  });

  const showGitHubOAuth = meta?.numericValue === 2;
  let gitHubOAuthHint = "";
  if (showGitHubOAuth) {
    gitHubOAuthHint = state.bootstrap?.gitHubOAuthEnabled
      ? "Use OAuth to avoid unauthenticated GitHub rate limits. A PAT still works as a manual fallback."
      : "GitHub OAuth is not configured for this app yet. Set gitHubOAuth.clientId in appsettings.json to enable browser authorization.";
  }

  elements.providerGitHubOAuthWrap.classList.toggle("hidden", !showGitHubOAuth);
  elements.providerGitHubOAuthStart.disabled = !showGitHubOAuth || !state.bootstrap?.gitHubOAuthEnabled;
  elements.providerGitHubOAuthHint.textContent = gitHubOAuthHint;

  elements.providerPatHint.textContent = buildProviderPatHint(meta);
  updateProviderClearTokenControls(meta);
  if (state.providerClearStoredToken) {
    setProviderStatus("Stored token will be cleared when you save this provider.", "info");
    return;
  }

  if (showGitHubOAuth && state.editingProviderGitHubAuthenticationMode === GITHUB_AUTH_MODE_OAUTH && state.editingProviderGitHubAuthenticatedUser) {
    setProviderStatus(`Stored OAuth token for @${state.editingProviderGitHubAuthenticatedUser}.`, "info");
    return;
  }

  setProviderStatus();
}

function getProviderFieldValues(providerMeta) {
  return Object.fromEntries(
    Object.entries(providerMeta.formValueFields).map(([fieldKey, providerKey]) => [
      providerKey,
      normalizeProviderField(PROVIDER_FORM_FIELDS[fieldKey].input.value)
    ])
  );
}

function syncProviderFieldValues(providerMeta, normalizedFieldValues) {
  Object.entries(providerMeta.formValueFields).forEach(([fieldKey, providerKey]) => {
    PROVIDER_FORM_FIELDS[fieldKey].input.value = normalizedFieldValues[providerKey]
      || PROVIDER_FORM_FIELDS[fieldKey].defaultValue
      || "";
  });
}

function validateGitHubOwnerType(providerMeta, gitHubOwnerType) {
  if (providerMeta.numericValue !== 2) {
    return null;
  }

  return Number.isInteger(gitHubOwnerType)
    ? null
    : "Select whether the GitHub owner is an organization or a user.";
}

function validateProviderPayloadInputs(providerMeta, values) {
  if (!values.displayName) {
    return "Display name is required.";
  }

  if (/[\\/]/.test(values.displayName)) {
    return "Display name cannot contain path separator characters.";
  }

  if (!values.organization) {
    return providerMeta.orgRequiredMessage;
  }

  if (providerMeta.visibleFields.includes("serverUrl")) {
    if (!values.serverUrl) {
      return "Server URL is required for Azure DevOps Server.";
    }

    const serverUrlError = validateProviderServerUrl(values.serverUrl);
    if (serverUrlError) {
      return serverUrlError;
    }
  }

  if (values.requirePersonalAccessToken && !values.personalAccessToken) {
    return "Enter a personal access token to test the connection.";
  }

  if (values.personalAccessToken && looksLikeProviderUrl(values.personalAccessToken)) {
    return "Personal access token looks like a URL. Check browser autofill and re-enter the token.";
  }

  return validateGitHubOwnerType(providerMeta, values.gitHubOwnerType);
}

function getGitHubAuthenticationMode(providerType, personalAccessToken) {
  if (providerType !== 2) {
    return GITHUB_AUTH_MODE_NONE;
  }

  if (personalAccessToken) {
    return GITHUB_AUTH_MODE_PAT;
  }

  if (!state.providerClearStoredToken
    && (state.providerGitHubOAuthToken || state.editingProviderGitHubAuthenticationMode === GITHUB_AUTH_MODE_OAUTH)) {
    return GITHUB_AUTH_MODE_OAUTH;
  }

  return GITHUB_AUTH_MODE_NONE;
}

function getGitHubAuthenticatedUser(providerType) {
  if (providerType !== 2) {
    return null;
  }

  if (state.providerGitHubOAuthToken) {
    return state.providerGitHubOAuthFlow?.authenticatedUser || null;
  }

  if (!state.providerClearStoredToken && state.editingProviderGitHubAuthenticationMode === GITHUB_AUTH_MODE_OAUTH) {
    return state.editingProviderGitHubAuthenticatedUser;
  }

  return null;
}

function shouldRetainGitHubToken(providerType, personalAccessToken) {
  return providerType === 2
    && !personalAccessToken
    && !state.providerClearStoredToken
    && !state.providerGitHubOAuthToken
    && state.editingProviderGitHubAuthenticationMode === GITHUB_AUTH_MODE_OAUTH;
}

export function toggleProviderClearStoredToken() {
  const providerMeta = getProviderMetaByRadioValue(getSelectedProviderRadioValue());
  if (!providerMeta || !state.editingProviderHasStoredToken) {
    return;
  }

  state.providerClearStoredToken = !state.providerClearStoredToken;
  state.providerConnectionTested = false;

  if (state.providerClearStoredToken) {
    elements.providerPat.value = "";
    clearGitHubOAuthState();
  }

  onProviderSetupTypeChange();
}

function collectProviderPayload(options = {}) {
  const personalAccessTokenStorageMode = Number.isInteger(options.personalAccessTokenStorageMode)
    ? options.personalAccessTokenStorageMode
    : state.editingProviderStorageMode;
  const providerMeta = getProviderMetaByRadioValue(getSelectedProviderRadioValue());
  if (!providerMeta) {
    return { payload: null, error: "Select a source control provider." };
  }

  const requirePersonalAccessToken = options.requirePersonalAccessToken === true
    && providerMeta.numericValue !== 2;

  const displayName = normalizeProviderField(elements.providerDisplayName.value);
  const personalAccessToken = normalizeProviderToken(elements.providerPat.value);
  const normalizedFieldValues = getProviderFieldValues(providerMeta);
  const organization = normalizedFieldValues.organization || "";
  const serverUrl = normalizedFieldValues.serverUrl || null;
  const gitHubOwnerType = Number.parseInt(normalizedFieldValues.gitHubOwnerType || "0", 10);

  elements.providerDisplayName.value = displayName;
  elements.providerPat.value = personalAccessToken;
  syncProviderFieldValues(providerMeta, normalizedFieldValues);

  const validationError = validateProviderPayloadInputs(providerMeta, {
    displayName,
    organization,
    serverUrl,
    personalAccessToken,
    requirePersonalAccessToken,
    gitHubOwnerType
  });
  if (validationError) {
    return { payload: null, error: validationError };
  }

  const gitHubAuthenticationMode = getGitHubAuthenticationMode(providerMeta.numericValue, personalAccessToken);
  const gitHubAuthenticatedUser = getGitHubAuthenticatedUser(providerMeta.numericValue);
  const clearPersonalAccessToken = state.providerClearStoredToken
    && !personalAccessToken
    && !state.providerGitHubOAuthToken;
  const retainPersonalAccessToken = clearPersonalAccessToken
    ? false
    : shouldRetainGitHubToken(providerMeta.numericValue, personalAccessToken);

  const payload = {
    provider: providerMeta.numericValue,
    displayName,
    personalAccessToken: personalAccessToken || state.providerGitHubOAuthToken || null,
    clearPersonalAccessToken,
    personalAccessTokenStorageMode,
    isEnabled: true,
    serverUrl,
    organizationUrl: null,
    organization,
    gitHubOwnerType: providerMeta.numericValue === 2 ? gitHubOwnerType : 0,
    gitHubAuthenticationMode,
    gitHubAuthenticatedUser,
    retainPersonalAccessToken
  };

  return { payload, error: null };
}

async function pollGitHubOAuthDeviceFlow(flowId, delayMs) {
  if (!flowId) {
    return;
  }

  if (state.providerGitHubOAuthPollHandle) {
    globalThis.clearTimeout(state.providerGitHubOAuthPollHandle);
  }

  state.providerGitHubOAuthPollHandle = globalThis.setTimeout(async () => {
    try {
      const result = await requestJson(`/api/providers/github/oauth/device-flow/${encodeURIComponent(flowId)}`);
      const existingFlow = state.providerGitHubOAuthFlow ?? {};
      state.providerGitHubOAuthFlow = {
        ...existingFlow,
        authenticatedUser: result.authenticatedUser || null,
        intervalSeconds: Number.isFinite(result.intervalSeconds) ? result.intervalSeconds : existingFlow.intervalSeconds || 5,
        nextPollAtUtc: result.nextPollAtUtc || null
      };

      if (result.status === "pending") {
        setProviderStatus(result.message || "Waiting for GitHub authorization to complete.", "info");
        void pollGitHubOAuthDeviceFlow(flowId, Math.max(1000, (result.intervalSeconds || 5) * 1000));
        return;
      }

      if (result.status === "authorized") {
        state.providerGitHubOAuthToken = result.accessToken || null;
        state.editingProviderGitHubAuthenticationMode = GITHUB_AUTH_MODE_OAUTH;
        state.editingProviderGitHubAuthenticatedUser = result.authenticatedUser || null;
        state.providerConnectionTested = true;
        elements.providerPat.value = "";
        elements.providerGitHubOAuthStart.disabled = false;
        setProviderStatus(result.message || "GitHub OAuth authorized.", "success");
        return;
      }

      state.providerGitHubOAuthToken = null;
      elements.providerGitHubOAuthStart.disabled = false;
      setProviderStatus(result.message || "GitHub OAuth authorization failed.", result.status === "error" ? "error" : "info");
    } catch (error) {
      state.providerGitHubOAuthToken = null;
      elements.providerGitHubOAuthStart.disabled = false;
      setProviderStatus(`GitHub OAuth failed: ${error.message}`, "error");
    }
  }, Math.max(0, delayMs || 0));
}

export async function startGitHubOAuth() {
  const providerMeta = getProviderMetaByRadioValue(getSelectedProviderRadioValue());
  if (providerMeta?.numericValue !== 2) {
    setProviderStatus("Select GitHub before starting OAuth.", "error");
    return;
  }

  if (!state.bootstrap?.gitHubOAuthEnabled) {
    setProviderStatus("GitHub OAuth is not configured for this app yet.", "error");
    return;
  }

  state.providerClearStoredToken = false;
  clearGitHubOAuthState();
  onProviderSetupTypeChange();
  elements.providerGitHubOAuthStart.disabled = true;
  setProviderStatus("Starting GitHub OAuth device flow...", "info");

  try {
    const result = await requestJson("/api/providers/github/oauth/device-flow", {
      method: "POST"
    });

    state.providerGitHubOAuthFlow = {
      flowId: result.flowId,
      authenticatedUser: null,
      intervalSeconds: Number.isFinite(result.intervalSeconds) ? result.intervalSeconds : 5
    };

    elements.providerGitHubOAuthLink.href = result.verificationUri;
    elements.providerGitHubOAuthLink.classList.remove("hidden");
    elements.providerGitHubOAuthCode.textContent = result.userCode || "";
    elements.providerGitHubOAuthCodeRow.classList.remove("hidden");
    elements.providerGitHubOAuthCopyNote.classList.add("hidden");
    if (await copyGitHubOAuthCodeToClipboard()) {
      elements.providerGitHubOAuthCopyNote.classList.remove("hidden");
    }
    setProviderStatus(`Enter code ${result.userCode} on GitHub to finish authorization.`, "info");
    globalThis.open(result.verificationUri, "_blank", "noopener");
    void pollGitHubOAuthDeviceFlow(result.flowId, 0);
  } catch (error) {
    elements.providerGitHubOAuthStart.disabled = false;
    setProviderStatus(`GitHub OAuth failed: ${error.message}`, "error");
  }
}

export async function testProviderConnection() {
  const { payload: config, error } = collectProviderPayload({ requirePersonalAccessToken: true });
  const btn = elements.btnTestProvider;

  if (!config) {
    setProviderStatus(error || "Select a provider, enter a display name, and fill in the required fields.", "error");
    return;
  }

  btn.disabled = true;
  setProviderStatus("Testing...", null);
  state.providerConnectionTested = false;

  try {
    const result = await requestJson("/api/providers/test", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(config)
    });
    if (result.success) {
      setProviderStatus(result.message || "Connection successful.", "success");
      state.providerConnectionTested = true;
    } else {
      setProviderStatus(`Connection failed: ${result.message}`, "error");
    }
  } catch (error) {
    setProviderStatus(`Connection failed: ${error.message}`, "error");
  } finally {
    btn.disabled = false;
  }
}

function shouldOfferPlainTextProviderFallback(error, payload) {
  return error?.status === 409
    && error?.data?.code === "pat-protection-unavailable"
    && payload.personalAccessTokenStorageMode !== PAT_STORAGE_MODE_PLAINTEXT;
}

function confirmPlainTextProviderFallback(warning) {
  return globalThis.confirm(`${warning}\n\nSelect OK to store the token in plain text for this provider, or Cancel to keep editing.`);
}

async function persistProviderWithFallback(payload) {
  let savePayload = { ...payload };

  while (true) {
    try {
      await requestJson("/api/providers", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(savePayload)
      });
      return savePayload;
    } catch (requestError) {
      if (!shouldOfferPlainTextProviderFallback(requestError, savePayload)) {
        throw requestError;
      }

      const warning = requestError.data.warning || "Secure token storage is unavailable on this platform.";
      const confirmed = confirmPlainTextProviderFallback(warning);
      if (!confirmed) {
        setProviderStatus("Provider was not saved. Secure token storage is unavailable on this platform.", "error");
        return null;
      }

      savePayload = {
        ...savePayload,
        personalAccessTokenStorageMode: PAT_STORAGE_MODE_PLAINTEXT
      };
      state.editingProviderStorageMode = PAT_STORAGE_MODE_PLAINTEXT;
    }
  }
}

export async function saveProvider() {
  const { payload, error } = collectProviderPayload({ requirePersonalAccessToken: false });

  if (!payload) {
    setProviderStatus(error || "Select a provider, enter a display name, and fill in the required fields.", "error");
    return;
  }

  if (state.editingProviderName
    && state.editingProviderName !== payload.displayName
    && !payload.personalAccessToken
    && !payload.clearPersonalAccessToken) {
    setProviderStatus("Enter a token or re-authorize when renaming a provider so the saved credential can be preserved.", "error");
    return;
  }

  elements.btnSaveProvider.disabled = true;

  try {
    const savePayload = await persistProviderWithFallback(payload);
    if (!savePayload) {
      return;
    }

    if (state.editingProviderName && state.editingProviderName !== savePayload.displayName) {
      await requestJson(`/api/providers/${encodeURIComponent(state.editingProviderName)}`, { method: "DELETE" });
    }

    await loadProviders();
    renderProviderList();
    closeProviderSetup();
  } catch (error) {
    setProviderStatus(`Save failed: ${error.message}`, "error");
  } finally {
    elements.btnSaveProvider.disabled = false;
  }
}

export async function confirmDeleteProvider(displayName) {
  if (!globalThis.confirm(`Delete provider "${displayName}"?`)) return;

  try {
    await requestJson(`/api/providers/${encodeURIComponent(displayName)}`, { method: "DELETE" });
    state.providers = state.providers.filter(p => p.displayName !== displayName);
    renderProviderList();
  } catch (error) {
    console.error("Delete provider failed:", error);
  }
}
