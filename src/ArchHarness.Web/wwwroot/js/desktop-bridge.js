export const desktopBridge = globalThis.archHarnessDesktop || null;
let keepAwakeActive = false;

export function syncKeepAwake(running) {
  if (!desktopBridge?.setKeepAwake) return;
  if (running === keepAwakeActive) return;
  keepAwakeActive = running;
  desktopBridge.setKeepAwake(running);
}

function setDesktopInset(name, value) {
  document.documentElement.style.setProperty(name, `${Math.max(0, Math.ceil(value))}px`);
}

export function applyDesktopChrome() {
  const root = document.documentElement;
  const chrome = desktopBridge?.chrome || null;
  if (!chrome) {
    return;
  }

  root.dataset.desktopPlatform = chrome.platform;

  if (!chrome.titleBarOverlay) {
    delete root.dataset.titleBarOverlay;
    return;
  }

  root.dataset.titleBarOverlay = "true";

  const overlay = navigator.windowControlsOverlay;
  const syncOverlayInsets = () => {
    let rightInset = 150;
    let topbarHeight = 46;

    if (overlay?.visible && typeof overlay.getTitlebarAreaRect === "function") {
      const rect = overlay.getTitlebarAreaRect();
      if (rect && Number.isFinite(rect.x) && Number.isFinite(rect.width)) {
        rightInset = Math.max(150, globalThis.innerWidth - (rect.x + rect.width));
      }

      if (rect && Number.isFinite(rect.height)) {
        topbarHeight = Math.max(46, rect.height);
      }
    }

    setDesktopInset("--desktop-right-inset", rightInset);
    setDesktopInset("--desktop-titlebar-height", topbarHeight);
  };

  syncOverlayInsets();
  overlay?.addEventListener?.("geometrychange", syncOverlayInsets);
  globalThis.addEventListener("resize", syncOverlayInsets);
}

export async function selectFolderWithDesktopBridge({ title, unavailableMessage, unavailableTarget }) {
  if (!desktopBridge?.selectFolder) {
    if (unavailableTarget) {
      unavailableTarget.textContent = unavailableMessage;
    }

    return null;
  }

  return desktopBridge.selectFolder({ title });
}
