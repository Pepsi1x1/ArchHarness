const { app, dialog } = require("electron");
const path = require("node:path");
const { WebHostManager } = require("./web-host-manager");
const { WindowManager } = require("./window-manager");
const ipcHandlers = require("./ipc-handlers");

// Disable the HTTP cache in dev mode so static asset changes are picked up immediately.
if (!app.isPackaged) {
  app.commandLine.appendSwitch("disable-http-cache");
}

// --- Composition root: wires abstractions to concrete implementations ---

const publishedWebHostDirectory = app.isPackaged
  ? path.join(process.resourcesPath, "web-host")
  : undefined;

const webHost = new WebHostManager({ publishedWebHostDirectory });
const windowManager = new WindowManager({ preloadPath: path.join(__dirname, "preload.js") });

webHost.on("host-error", message => {
  dialog.showErrorBox("ArchHarness Web Host Stopped", message);
});

ipcHandlers.registerAll({
  windowProvider: () => windowManager.mainWindow
});

let shutdownComplete = false;

app.on("window-all-closed", () => {
  app.quit();
});

app.on("before-quit", event => {
  if (shutdownComplete) {
    return;
  }

  event.preventDefault();

  void webHost.stop().finally(() => {
    shutdownComplete = true;
    app.exit(0);
  });
});

app.on("activate", () => {
  if (!windowManager.hasWindows()) {
    windowManager.createMainWindow(webHost.hostUrl);
  }
});

app.whenReady().then(async () => {
  try {
    await webHost.ensure();
    windowManager.createMainWindow(webHost.hostUrl);
  } catch (error) {
    dialog.showErrorBox("ArchHarness failed to start", error instanceof Error ? error.message : String(error));
    app.quit();
  }
});
