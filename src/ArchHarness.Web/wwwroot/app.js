import { MAIN_PANEL_VIEWS } from './js/constants.js';
import { state, elements } from './js/state.js';
import { requestJson } from './js/api.js';
import { applyDesktopChrome, desktopBridge } from './js/desktop-bridge.js';
import { saveShellState, restoreShellState } from './js/shell-persistence.js';
import { openModal, closeModal, registerModalPreClose } from './js/modals.js';
import {
  renderComposerState, clearLegacyAutofillPrompt,
  getComposerDropdownConfigs, closeComposerDropdowns, toggleComposerDropdown
} from './js/composer.js';
import { connectEventStream, closeEventStream } from './js/stream.js';
import { closeWorkspaceBranchMenu, toggleWorkspaceBranchMenu } from './js/branch.js';
import {
  renderMainPanelView, setMainPanelView,
  loadBranchChangesForActiveProject, stashGitChangesAndContinue
} from './js/git-changes.js';
import {
  startRun, pauseRun, cancelRun, resumeSelectedRun,
  startImplementationFromPlanningRun, renderRunDetailsActions,
  refreshActiveRun, loadSelectedRunStream
} from './js/runs.js';
import { loadBootstrap, loadProjects, createProject, pickProjectFolder } from './js/projects.js';
import {
  loadSettings, renderSettingsForm, applySettingsDefaults,
  saveSettings, switchSettingsTab, handleSettingsTabKeydown, closeSettingsDropdowns
} from './js/settings.js';
import {
  renderInlineInteraction, pollPendingInteraction,
  clearPendingInteractionPoll, abortPendingInteractionPoll, handleVisibilityChange
} from './js/interactions.js';
import {
  loadProviders, openProviderSetup, closeProviderSetup,
  testProviderConnection, startGitHubOAuth, toggleProviderClearStoredToken,
  copyGitHubOAuthCodeToClipboard, saveProvider, onProviderSetupTypeChange,
  setProviderStatus
} from './js/providers.js';
import {
  openReviewPrModal, handleReviewPrBack, startPullRequestReview,
  handleReviewPrNext, clearPullRequestFilter, pickReviewPrFolder
} from './js/pull-request-review.js';

registerModalPreClose("settings-modal", () => {
  closeSettingsDropdowns();
  closeProviderSetup();
});

async function warmModelDiscovery() {
  try {
    await requestJson("/api/preflight");
  } catch {
    // Model discovery is opportunistic for settings UX; the shell can still run on configured fallbacks.
  }
}

function attachHandlers() {
  elements.newProjectButton.addEventListener("click", () => openModal("new-project-modal"));

  const wikidocScreenButton = document.getElementById("wikidoc-screen-button");
  if (desktopBridge?.openWikiDocScreen) {
    wikidocScreenButton.addEventListener("click", () => {
      void desktopBridge.openWikiDocScreen();
    });
  } else {
    wikidocScreenButton.addEventListener("click", () => {
      globalThis.open("/wikidoc.html", "_blank");
    });
  }
  elements.pickProjectFolder.addEventListener("click", () => {
    void pickProjectFolder().catch(error => {
      elements.projectPickerNote.textContent = `Folder selection failed: ${error.message}`;
    });
  });
  elements.reviewPrPickFolder.addEventListener("click", () => {
    void pickReviewPrFolder().catch(error => {
      const hintEl = document.getElementById("review-pr-folder-hint");
      if (hintEl) {
        hintEl.textContent = `Folder selection failed: ${error.message}`;
      }
    });
  });
  elements.settingsButton.addEventListener("click", () => {
    renderSettingsForm();
    applySettingsDefaults();
    switchSettingsTab("agent-settings");
    closeProviderSetup();
    void loadProviders();
    openModal("settings-modal");
  });
  elements.streamSections.addEventListener("scroll", () => {
    const el = elements.streamSections;
    const atBottom = el.scrollHeight - el.scrollTop - el.clientHeight < 40;
    state.streamAutoScroll = atBottom;
  });
  elements.startRun.addEventListener("click", () => startRun().catch(error => {
    console.error("Run submission failed:", error);
  }));
  elements.pauseRun?.addEventListener("click", () => pauseRun().catch(error => {
    console.error("Pause failed:", error);
  }));
  elements.streamViewButton?.addEventListener("click", () => {
    setMainPanelView(MAIN_PANEL_VIEWS.STREAM, { persist: true });
  });
  elements.branchChangesViewButton?.addEventListener("click", () => {
    setMainPanelView(MAIN_PANEL_VIEWS.BRANCH_CHANGES, { persist: true, forceRefresh: true });
  });
  elements.branchChangesRefresh?.addEventListener("click", () => {
    void loadBranchChangesForActiveProject({ force: true }).catch(error => {
      console.error("Branch changes refresh failed:", error);
    });
  });
  elements.cancelRun.addEventListener("click", () => cancelRun().catch(error => {
    console.error("Cancel failed:", error);
  }));
  elements.resumeRun?.addEventListener("click", () => resumeSelectedRun().catch(error => {
    console.error("Resume failed:", error);
    renderRunDetailsActions();
  }));
  elements.implementRun?.addEventListener("click", () => startImplementationFromPlanningRun().catch(error => {
    console.error("Implementation handoff failed:", error);
    renderRunDetailsActions();
  }));
  elements.taskPrompt.addEventListener("input", () => {
    saveShellState();
    renderComposerState();
  });
  [elements.runMode, elements.permissionMode, elements.architectureReviewPreset].forEach(control => {
    control.addEventListener("input", () => {
      saveShellState();
      renderComposerState();
    });
    control.addEventListener("change", () => {
      saveShellState();
      renderComposerState();
    });
  });

  document.querySelectorAll("[data-close-modal]").forEach(button => {
    button.addEventListener("click", closeModal);
  });
  elements.modalBackdrop.addEventListener("click", closeModal);
  document.querySelectorAll(".settings-tab").forEach(btn => {
    btn.addEventListener("click", () => switchSettingsTab(btn.dataset.tab));
    btn.addEventListener("keydown", handleSettingsTabKeydown);
  });
  elements.btnAddProvider.addEventListener("click", () => openProviderSetup());
  elements.btnCancelProvider.addEventListener("click", closeProviderSetup);
  elements.btnTestProvider.addEventListener("click", () => {
    void testProviderConnection().catch(error => console.error("Provider test failed:", error));
  });
  elements.providerGitHubOAuthStart.addEventListener("click", () => {
    void startGitHubOAuth().catch(error => console.error("GitHub OAuth failed:", error));
  });
  elements.providerPatClear.addEventListener("click", toggleProviderClearStoredToken);
  elements.providerGitHubOAuthCopy.addEventListener("click", () => {
    void copyGitHubOAuthCodeToClipboard().then(copied => {
      if (!copied) {
        setProviderStatus("Unable to copy the GitHub device code to the clipboard. Copy it manually and continue in the browser.", "error");
      }
    });
  });
  elements.btnSaveProvider.addEventListener("click", () => {
    void saveProvider().catch(error => console.error("Save provider failed:", error));
  });
  elements.providerTypeRadios.forEach(radio => {
    radio.addEventListener("change", onProviderSetupTypeChange);
  });
  [elements.providerDisplayName, elements.providerServerUrl, elements.providerOrg, elements.providerPat].forEach(input => {
    input.addEventListener("input", () => {
      if (input === elements.providerPat && state.providerClearStoredToken && elements.providerPat.value.trim()) {
        state.providerClearStoredToken = false;
      }

      state.providerConnectionTested = false;
      onProviderSetupTypeChange();
    });
  });
  elements.providerPatToggle.addEventListener("click", () => {
    const masked = elements.providerPat.type !== "password";
    elements.providerPat.type = masked ? "password" : "text";
    elements.providerPatToggleIcon.className = masked ? "fa-solid fa-eye" : "fa-solid fa-eye-slash";
    elements.providerPatToggle.setAttribute("aria-label", masked ? "Show token" : "Hide token");
    elements.providerPatToggle.setAttribute("aria-pressed", masked ? "false" : "true");
  });
  elements.newProjectForm.addEventListener("submit", event => {
    void createProject(event).catch(error => {
      console.error("Project creation failed:", error);
    });
  });
  elements.settingsForm.addEventListener("submit", event => {
    void saveSettings(event).catch(error => {
      console.error("Saving settings failed:", error);
    });
  });

  document.addEventListener("visibilitychange", handleVisibilityChange);
  document.addEventListener("click", event => {
    if (!elements.workspaceBranchWrap.contains(event.target)) {
      closeWorkspaceBranchMenu();
    }

    const composerDropdownClicked = getComposerDropdownConfigs().some(config => config.wrap.contains(event.target))
      || elements.architectureReviewAgentsWrap.contains(event.target);
    if (!composerDropdownClicked) {
      closeComposerDropdowns();
    }

    if (!event.target.closest(".settings-dropdown")) {
      closeSettingsDropdowns();
    }
  });
  document.addEventListener("keydown", event => {
    if (event.key === "Escape") {
      closeWorkspaceBranchMenu();
      closeComposerDropdowns();
      closeSettingsDropdowns();
    }
  });

  document.getElementById("review-pr-button").addEventListener("click", () => {
    void openReviewPrModal();
  });
  elements.workspaceBranchButton.addEventListener("click", event => {
    event.stopPropagation();
    toggleWorkspaceBranchMenu();
  });
  elements.runModeButton.addEventListener("click", event => {
    event.stopPropagation();
    toggleComposerDropdown("run-mode");
  });
  elements.permissionModeButton.addEventListener("click", event => {
    event.stopPropagation();
    toggleComposerDropdown("permission-mode");
  });
  elements.architectureReviewPresetButton.addEventListener("click", event => {
    event.stopPropagation();
    toggleComposerDropdown("architecture-review-preset");
  });
  elements.architectureReviewAgentsButton.addEventListener("click", event => {
    event.stopPropagation();
    toggleComposerDropdown("architecture-review-agents");
  });
  elements.gitChangesStashButton.addEventListener("click", () => {
    void stashGitChangesAndContinue().catch(error => {
      console.error("Stash and switch failed:", error);
    });
  });
  document.getElementById("review-pr-close-button").addEventListener("click", closeModal);
  document.getElementById("review-pr-back-button").addEventListener("click", handleReviewPrBack);
  elements.reviewPrGoButton.addEventListener("click", () => {
    void startPullRequestReview().catch(error => {
      console.error("PR architecture review failed:", error);
    });
  });
  document.getElementById("review-pr-next-button").addEventListener("click", () => {
    void handleReviewPrNext().catch(error => {
      console.error("PR review step failed:", error);
    });
  });
  document.getElementById("pr-filter-project-clear").addEventListener("click", () => clearPullRequestFilter("project"));
  document.getElementById("pr-filter-repo-clear").addEventListener("click", () => clearPullRequestFilter("repository"));
  document.getElementById("pr-filter-author-clear").addEventListener("click", () => clearPullRequestFilter("author"));
}

async function init() {
  applyDesktopChrome();
  attachHandlers();
  restoreShellState();
  clearLegacyAutofillPrompt();
  await Promise.all([loadBootstrap(), warmModelDiscovery()]);
  await loadSettings();
  await loadProjects();
  await refreshActiveRun();
  await loadSelectedRunStream();
  renderMainPanelView();
  renderInlineInteraction();
  connectEventStream();
  await pollPendingInteraction();
}

globalThis.addEventListener("beforeunload", () => {
  state.isUnloading = true;
  closeEventStream();
  clearPendingInteractionPoll();
  abortPendingInteractionPoll();
});

try {
  await init();
} catch (error) {
  console.error("Initialization failed:", error);
}
