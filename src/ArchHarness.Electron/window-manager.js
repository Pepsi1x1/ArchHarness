const { BrowserWindow, shell } = require("electron");
const path = require("node:path");

/**
 * Manages Electron BrowserWindow creation and lifecycle.
 * Single Responsibility: only window management concerns.
 */
class WindowManager {
  #preloadPath;
  #windowIconPath;
  #mainWindow = null;

  constructor({ preloadPath, windowIconPath }) {
    this.#preloadPath = preloadPath;
    this.#windowIconPath = windowIconPath;
  }

  get mainWindow() {
    return this.#mainWindow;
  }

  createMainWindow(loadUrl) {
    this.#mainWindow = this.#createWindow({
      width: 1600,
      height: 1040,
      minWidth: 1200,
      minHeight: 800,
      title: "ArchHarness"
    });

    this.#mainWindow.loadURL(loadUrl).catch(error => {
      console.error("Failed to load main window URL.", { loadUrl, error });
    });

    this.#mainWindow.on("closed", () => {
      this.#mainWindow = null;
    });

    return this.#mainWindow;
  }

  createWikiDocWindow(loadUrl) {
    const wikiWindow = this.#createWindow({
      width: 1100,
      height: 820,
      minWidth: 720,
      minHeight: 540,
      title: "ArchHarness \u2013 Wiki Docs"
    });

    wikiWindow.loadURL(loadUrl).catch(error => {
      console.error("Failed to load Wiki Docs window URL.", { loadUrl, error });
    });

    return wikiWindow;
  }

  #createWindow({ width, height, minWidth, minHeight, title }) {
    const isMac = process.platform === "darwin";
    const isWindows = process.platform === "win32";

    const win = new BrowserWindow({
      width,
      height,
      minWidth,
      minHeight,
      autoHideMenuBar: true,
      backgroundColor: "#08121c",
      ...(isMac
        ? {
            titleBarStyle: "hiddenInset",
            trafficLightPosition: { x: 14, y: 13 }
          }
        : {}),
      ...(isWindows
        ? {
            titleBarStyle: "hidden",
            titleBarOverlay: {
              color: "#08121c",
              symbolColor: "#f4f5f7",
              height: 46
            }
          }
        : {}),
      title,
      icon: this.#windowIconPath,
      webPreferences: {
        contextIsolation: true,
        nodeIntegration: false,
        sandbox: true,
        preload: this.#preloadPath
      }
    });

    win.webContents.setWindowOpenHandler(({ url }) => {
      try {
        const parsed = new URL(url);
        if (parsed.protocol === "https:") {
          void shell.openExternal(url);
        }
      } catch {
        // Ignore malformed URLs
      }
      return { action: "deny" };
    });

    return win;
  }

  hasWindows() {
    return BrowserWindow.getAllWindows().length > 0;
  }
}

module.exports = { WindowManager };
