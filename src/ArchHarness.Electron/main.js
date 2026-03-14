const { app, BrowserWindow, dialog, shell } = require("electron");
const { spawn } = require("node:child_process");
const fs = require("node:fs");
const path = require("node:path");

const HOST_URL = process.env.ARCHHARNESS_WEB_URL || "http://127.0.0.1:5057";
const HEALTH_URL = `${HOST_URL}/api/health`;
const REPO_ROOT = path.resolve(__dirname, "..", "..");
const WEB_PROJECT_PATH = path.join(REPO_ROOT, "src", "ArchHarness.Web", "ArchHarness.Web.csproj");
const STARTUP_TIMEOUT_MS = 45000;
const HEALTH_POLL_MS = 500;

let mainWindow = null;
let webHostProcess = null;
let ownsWebHostProcess = false;
let shuttingDown = false;
let shutdownComplete = false;

function canLaunchLocalWebHost() {
  return fs.existsSync(WEB_PROJECT_PATH);
}

async function isWebHostHealthy() {
  try {
    const response = await fetch(HEALTH_URL, { cache: "no-store" });
    if (!response.ok) {
      return false;
    }

    const payload = await response.json();
    return payload?.healthy === true;
  } catch {
    return false;
  }
}

function sleep(milliseconds) {
  return new Promise(resolve => setTimeout(resolve, milliseconds));
}

async function waitForWebHostReady() {
  const startedAt = Date.now();

  while ((Date.now() - startedAt) < STARTUP_TIMEOUT_MS) {
    if (await isWebHostHealthy()) {
      return;
    }

    if (webHostProcess && webHostProcess.exitCode !== null) {
      throw new Error(`ArchHarness web host exited early with code ${webHostProcess.exitCode}.`);
    }

    await sleep(HEALTH_POLL_MS);
  }

  throw new Error(`Timed out waiting for ArchHarness web host at ${HOST_URL}.`);
}

function startLocalWebHost() {
  if (webHostProcess || !canLaunchLocalWebHost()) {
    return;
  }

  const environment = {
    ...process.env,
    webHost__url: HOST_URL
  };

  webHostProcess = spawn("dotnet", ["run", "--project", WEB_PROJECT_PATH, "--no-launch-profile"], {
    cwd: REPO_ROOT,
    env: environment,
    stdio: ["ignore", "pipe", "pipe"]
  });

  ownsWebHostProcess = true;

  webHostProcess.stdout.on("data", chunk => {
    process.stdout.write(`[archharness-web] ${chunk}`);
  });

  webHostProcess.stderr.on("data", chunk => {
    process.stderr.write(`[archharness-web] ${chunk}`);
  });

  webHostProcess.once("exit", code => {
    if (!shuttingDown && code !== 0) {
      dialog.showErrorBox("ArchHarness Web Host Stopped", `The local web host exited with code ${code}.`);
    }

    webHostProcess = null;
    ownsWebHostProcess = false;
  });
}

async function ensureWebHost() {
  if (await isWebHostHealthy()) {
    ownsWebHostProcess = false;
    return;
  }

  if (!canLaunchLocalWebHost()) {
    throw new Error(`Unable to find ${WEB_PROJECT_PATH}.`);
  }

  startLocalWebHost();
  await waitForWebHostReady();
}

function createMainWindow() {
  mainWindow = new BrowserWindow({
    width: 1600,
    height: 1040,
    minWidth: 1200,
    minHeight: 800,
    autoHideMenuBar: true,
    backgroundColor: "#08121c",
    title: "ArchHarness",
    webPreferences: {
      contextIsolation: true,
      nodeIntegration: false,
      preload: path.join(__dirname, "preload.js")
    }
  });

  mainWindow.webContents.setWindowOpenHandler(({ url }) => {
    void shell.openExternal(url);
    return { action: "deny" };
  });

  void mainWindow.loadURL(HOST_URL);

  mainWindow.on("closed", () => {
    mainWindow = null;
  });
}

async function stopWebHost() {
  if (!webHostProcess || !ownsWebHostProcess) {
    return;
  }

  const processToStop = webHostProcess;
  webHostProcess = null;
  ownsWebHostProcess = false;
  shuttingDown = true;

  if (processToStop.exitCode === null) {
    processToStop.kill("SIGTERM");
    await sleep(750);
  }

  if (processToStop.exitCode === null) {
    processToStop.kill("SIGKILL");
  }
}

app.on("window-all-closed", () => {
  app.quit();
});

app.on("before-quit", event => {
  if (shutdownComplete) {
    return;
  }

  event.preventDefault();
  shuttingDown = true;

  void stopWebHost().finally(() => {
    shutdownComplete = true;
    app.exit(0);
  });
});

app.on("activate", () => {
  if (BrowserWindow.getAllWindows().length === 0) {
    createMainWindow();
  }
});

app.whenReady().then(async () => {
  try {
    await ensureWebHost();
    createMainWindow();
  } catch (error) {
    dialog.showErrorBox("ArchHarness failed to start", error instanceof Error ? error.message : String(error));
    app.quit();
  }
});
