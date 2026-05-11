import { beforeEach, describe, expect, it, vi } from 'vitest';
import { GITHUB_AUTH_MODE_OAUTH, GITHUB_AUTH_MODE_PAT, PAT_STORAGE_MODE_PROTECTED } from '../wwwroot/js/constants.js';

const requestJsonMock = vi.fn();

vi.mock('../wwwroot/js/api.js', () => ({
  requestJson: requestJsonMock
}));

function installProviderDom() {
  document.body.innerHTML = `
    <div id="provider-list"></div>
    <section id="provider-setup" class="hidden"></section>
    <button id="btn-add-provider"></button>
    <button id="btn-test-provider"></button>
    <button id="btn-save-provider"></button>
    <button id="btn-cancel-provider"></button>
    <div id="provider-test-status"></div>
    <label><input name="pf-type" type="radio" value="ado-server"></label>
    <label><input name="pf-type" type="radio" value="ado-services"></label>
    <label><input name="pf-type" type="radio" value="github"></label>
    <input id="pf-display-name">
    <div id="pf-server-url-wrap"><input id="pf-server-url"></div>
    <div id="pf-org-wrap"><label id="pf-org-label"></label><input id="pf-org"></div>
    <div id="pf-github-owner-type-wrap"><select id="pf-github-owner-type"><option value="0">Org</option><option value="1">User</option></select></div>
    <div id="pf-pat-wrap"><input id="pf-pat"><div id="pf-pat-hint"></div></div>
    <button id="pf-clear-token"></button><div id="pf-clear-token-note" class="hidden"></div>
    <button id="pf-pat-toggle"><i id="pf-pat-toggle-icon"></i></button>
    <div id="pf-github-oauth-wrap" class="hidden">
      <button id="pf-github-oauth-start"></button>
      <a id="pf-github-oauth-link" class="hidden"></a>
      <div id="pf-github-oauth-code-row" class="hidden"><code id="pf-github-oauth-code"></code><button id="pf-github-oauth-copy"></button></div>
      <div id="pf-github-oauth-copy-note" class="hidden"></div>
      <div id="pf-github-oauth-hint"></div>
    </div>
  `;
}

async function loadProviderModules() {
  vi.resetModules();
  installProviderDom();
  const stateModule = await import('../wwwroot/js/state.js');
  const providerModule = await import('../wwwroot/js/providers.js');
  return { ...stateModule, ...providerModule };
}

beforeEach(() => {
  requestJsonMock.mockReset();
});

describe('provider management', () => {
  it('normalizes provider and review lookup inputs', async () => {
    const { normalizeProviderField, normalizeReviewLookupValue, normalizeReviewPullRequestId, getProviderMetaByType } = await loadProviderModules();

    expect(normalizeProviderField('  Org\nName\t ')).toBe('Org Name');
    expect(normalizeReviewLookupValue('  abc\0def  ', 6)).toBe('abc de');
    expect(normalizeReviewPullRequestId(' 123 ')).toBe('123');
    expect(normalizeReviewPullRequestId('PR-123')).toBe('');
    expect(getProviderMetaByType(2)?.label).toBe('GitHub');
  });

  it('normalizes provider collections from arrays or response wrappers', async () => {
    const { normalizeProviderCollection } = await loadProviderModules();

    expect(normalizeProviderCollection({ providers: [
      { providerType: 2, displayName: ' GitHub ', organization: 'octo-org', gitHubAuthenticationMode: GITHUB_AUTH_MODE_OAUTH, gitHubAuthenticatedUser: 'octocat' },
      { providerType: 99, displayName: 'Unknown' }
    ] })).toEqual([{ providerType: 2, displayName: 'GitHub', serverUrl: null, organization: 'octo-org', gitHubOwnerType: 0, gitHubAuthenticationMode: GITHUB_AUTH_MODE_OAUTH, gitHubAuthenticatedUser: 'octocat', personalAccessToken: null, hasStoredPersonalAccessToken: false, personalAccessTokenStorageMode: PAT_STORAGE_MODE_PROTECTED, isEnabled: true }]);
  });

  it('renders provider list badges and edit/delete actions', async () => {
    const { state, elements, renderProviderList } = await loadProviderModules();
    state.providers = [
      { providerType: 2, displayName: 'GitHub', gitHubAuthenticationMode: GITHUB_AUTH_MODE_OAUTH, gitHubAuthenticatedUser: 'octocat', hasStoredPersonalAccessToken: true },
      { providerType: 1, displayName: 'ADO', gitHubAuthenticationMode: 0, hasStoredPersonalAccessToken: true }
    ];

    renderProviderList();

    expect(elements.providerList.querySelectorAll('.provider-item')).toHaveLength(2);
    expect(elements.providerList.textContent).toContain('GitHub');
    expect(elements.providerList.textContent).toContain('Protected OAuth');
    expect(elements.providerList.textContent).toContain('@octocat');
    expect(elements.providerList.textContent).toContain('Azure DevOps Services');
    expect(elements.providerList.textContent).toContain('Protected PAT');
  });

  it('opens GitHub setup with OAuth controls and clear-token state', async () => {
    const { state, elements, openProviderSetup, toggleProviderClearStoredToken } = await loadProviderModules();
    state.bootstrap = { gitHubOAuthEnabled: true };

    openProviderSetup({
      providerType: 2,
      displayName: 'GitHub',
      organization: 'octo-org',
      gitHubOwnerType: 1,
      gitHubAuthenticationMode: GITHUB_AUTH_MODE_PAT,
      hasStoredPersonalAccessToken: true
    });

    expect(elements.providerSetup.classList.contains('hidden')).toBe(false);
    expect(elements.providerDisplayName.value).toBe('GitHub');
    expect(elements.providerOrg.value).toBe('octo-org');
    expect(elements.providerGitHubOAuthWrap.classList.contains('hidden')).toBe(false);
    expect(elements.providerGitHubOAuthStart.disabled).toBe(false);
    expect(elements.providerPatClear.classList.contains('hidden')).toBe(false);

    toggleProviderClearStoredToken();
    expect(state.providerClearStoredToken).toBe(true);
    expect(elements.providerPatClearNote.classList.contains('hidden')).toBe(false);
    expect(elements.providerTestStatus.textContent).toContain('Stored token will be cleared');
  });
});