# Hosts and User Interfaces

## Relevant files

| Path | Why it matters |
| --- | --- |
| `src\ArchHarness.Console\Program.cs` | Console host composition root. |
| `src\ArchHarness.Web\Program.cs` | Web host startup and middleware pipeline. |
| `src\ArchHarness.Web\ProgramEndpointExtensions.cs` | Complete API route map. |
| `src\ArchHarness.Web\Services\WebRunSessionManager.cs` | Active-run ownership, pause/cancel, and event rebroadcasting. |
| `src\ArchHarness.Web\wwwroot\app.js` | Main browser control-room shell. |
| `src\ArchHarness.Web\wwwroot\js\wikidoc-screen.js` | Dedicated WikiDoc screen behavior. |
| `src\ArchHarness.Electron\main.js` | Electron composition root. |
| `src\ArchHarness.Electron\web-host-manager.js` | Launches or reuses the local web host and waits for health. |

ArchHarness ships three user-facing hosts over the same runtime: a console/TUI shell, a loopback-only web host, and an Electron wrapper around the web host.

## Console host

`src\ArchHarness.Console\Program.cs` is intentionally thin:

- It calls `AddArchHarnessRuntimeServices` and `AddArchHarnessInteractiveServices`.
- It binds console-specific bridges: `ConsoleSetupStatusSink`, `ConsoleCopilotUserInputBridge`, and `ConsolePlanApprovalBridge`.
- It resolves `ChatTerminal` as the `IApplicationHost`.

Operationally, the console host supports two modes:

1. **Interactive TUI** - field-based setup, confirmation, and run monitoring.
2. **Scriptable CLI** - `run ...` or `wikidoc ...` commands for automation.

The CLI parser lives in `src\ArchHarness.App\Core\CliArgumentParser.cs` and supports:

- `run <taskPrompt> <workspacePath> <workspaceMode> [workflow] [projectName] [modelOverrides] [buildCommand]`
- `wikidoc <scanRoot> [projectName] [modelOverrides]`

When architecture-loop mode is enabled in settings, the parser also supports a promptless shorthand:

- `run <workspacePath> <workspaceMode> architecture-loop`

`wikidoc` is explicitly treated as a non-interactive command, so it skips the normal setup confirmation and post-run navigation screens.

## Web host

`src\ArchHarness.Web\Program.cs` creates a local-only ASP.NET Core host and builds a sanitized Markdown pipeline:

- `UseAdvancedExtensions()` is enabled for Markdown rendering.
- Raw HTML is disabled with `DisableHtml()`.

### Local-only security boundary

The web host is intentionally bound to loopback:

- `ConfigureArchHarnessWebHost` only accepts `webHost:url` values that point to `localhost` or a loopback IP.
- The committed default is `http://127.0.0.1:5057`.
- `UseArchHarnessLocalOnlyAccessControl` returns HTTP 403 for non-loopback clients.
- `UseArchHarnessSecurityHeaders` adds CSP, `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, `Referrer-Policy`, and a locked-down permissions policy.

### API surface

`ProgramEndpointExtensions.MapArchHarnessApi` exposes the full control plane:

| Area | Representative endpoints |
| --- | --- |
| Bootstrap and health | `GET /api`, `GET /api/bootstrap`, `GET /api/workflows`, `GET /api/health` |
| Project and Git operations | `GET/POST /api/projects`, `GET /api/projects/{projectId}/branch`, `GET /api/projects/{projectId}/git/changes`, `GET /api/projects/{projectId}/git/diff`, `POST /api/projects/{projectId}/git/stash`, `POST /api/projects/{projectId}/git/clone`, `POST /api/projects/{projectId}/branch` |
| Settings and model UX | `GET/PUT /api/settings`, `GET /api/models`, `GET /api/preflight`, `POST /api/setup-summary`, `POST /api/markdown/render` |
| Provider connections and PR lookup | `GET/POST/DELETE /api/providers`, `POST /api/providers/test`, GitHub device-flow endpoints, provider PR lookup endpoints, and PR file listing |
| Run history and lifecycle | `GET /api/runs`, `GET /api/runs/{runId}/artifacts`, `GET /api/runs/{runId}/events`, `GET /api/runs/{runId}/state`, `POST /api/runs`, `POST /api/runs/{runId}/resume`, `POST /api/runs/{runId}/handoff`, `POST /api/runs/active/pause`, `DELETE /api/runs/active` |
| Live stream and interaction bridges | `GET /api/runs/active`, `GET /api/runs/active/events`, `GET /api/interactions/pending`, `POST /api/interactions/user-input`, `POST /api/interactions/permission`, `POST /api/interactions/plan-approval` |

### Active-run session model

`WebRunSessionManager` is designed around a single active run at a time:

- It owns one active `CancellationTokenSource`.
- It resets and rebroadcasts events through a shared event hub.
- It exposes pause, cancel, resume, and active snapshot operations.
- It continuously pumps both agent delta events and Copilot session lifecycle events into the web stream.

`WebRunExecutionRunner` turns runtime progress into browser-visible events, updates terminal run state on pause/cancel/failure, and persists `paused`, `canceled`, `stopped`, or `failed` outcomes back into `run-state.json`.

## Browser control room

The browser UI is served from `src\ArchHarness.Web\wwwroot\`.

`app.js` is the top-level shell that wires together project loading, settings, event streaming, Git branch/change views, provider setup, pending interactions, run lifecycle actions, and pull-request review flows. Important behaviors include:

- opportunistic model discovery via `/api/preflight`
- shell-state persistence across reloads
- inline handling of permission prompts and user-input questions
- handoff from planning runs into implementation runs
- separate main-panel views for run stream versus branch changes

## Dedicated WikiDoc screen

`wikidoc.html` plus `js\wikidoc-screen.js` provide a focused documentation-generation surface:

- It launches a normal run through `POST /api/runs`.
- The request uses `workflow: "wikidoc"` and `workspaceMode: "existing-folder"`.
- It reuses the same `/api/runs/active/events` stream as the main shell.
- It renders agent deltas, prompt segments, reasoning sections, and grouped tool calls without introducing a second transport layer.

In Electron, the screen opens in a separate native window. In the browser-only host, it opens as `/wikidoc.html` in a new tab.

## Electron wrapper

The Electron app in `src\ArchHarness.Electron\` is a desktop shell around the web host, not a second implementation of the runtime.

### Startup behavior

`main.js` creates three core objects:

- `WebHostManager`
- `WindowManager`
- `ipc-handlers`

`WebHostManager` first checks whether `http://127.0.0.1:5057/api/health` is already healthy. If not, it tries:

1. a published `ArchHarness.Web` executable under `build\web-host\` (or packaged resources), then
2. `dotnet run --project src\ArchHarness.Web\ArchHarness.Web.csproj --no-launch-profile`

It waits up to **45 seconds** for `/api/health`, polling every **500 ms**.

### Desktop security and UX choices

`WindowManager` creates sandboxed windows with:

- `contextIsolation: true`
- `nodeIntegration: false`
- `sandbox: true`
- preload-only access through `preload.js`

External window opens are denied by default; only `https:` links are handed off to the operating system via `shell.openExternal`.

Platform-specific chrome is also applied:

- macOS uses `hiddenInset` title bars with traffic-light positioning.
- Windows uses `titleBarOverlay` with a custom dark header.

### IPC bridge

`ipc-handlers.js` exposes a small desktop-only bridge:

| Channel | Purpose |
| --- | --- |
| `archharness:set-keep-awake` | Prevent or release display sleep while a run is active. |
| `archharness:pick-folder` | Native folder picker for project and WikiDoc scan-root selection. |
| `archharness:open-wikidoc-screen` | Open the dedicated WikiDoc window. |

## See also

- [WikiDoc Workflow](WikiDoc-Workflow.md)
- [Source Control and Providers](Source-Control-and-Providers.md)
- [Storage, Run Artifacts, and State](Storage-Run-Artifacts-and-State.md)
