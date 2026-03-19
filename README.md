# ArchHarness

ArchHarness is a .NET application suite that runs a multi-agent software workflow on top of GitHub Copilot SDK sessions.

It ships with a shared runtime library plus three supported hosts: a console host for the terminal-first workflow, a local ASP.NET Core web host for the browser control room, and an Electron wrapper that presents that same web UI in a native window.

## What It Does

- Accepts a task prompt and workspace target.
- Builds an execution plan with an orchestration agent.
- Delegates implementation/review steps to specialized agents:
	- `Orchestration`: planning and completion validation
	- `FrontendDeveloper`: frontend implementation changes
	- `BackendDeveloper`: backend implementation changes
	- `Build`: baseline/intermediate build execution and build-result triage
	- `Architecture`: architecture enforcement and findings
- Optionally loops architecture remediation until high-severity findings are cleared (or max iterations reached).
- Runs build validation and records final status.
- Persists run artifacts under `.agent-harness/runs/<runId>/` in the target workspace.

## Repository Layout

- `src/ArchHarness.App/`: shared runtime, agents, Copilot integration, storage, and TUI components
- `src/ArchHarness.Console/`: console entry point for the existing interactive and scriptable workflow
- `src/ArchHarness.Web/`: local ASP.NET Core host and browser-first control-room UI
- `src/ArchHarness.Electron/`: Electron desktop wrapper that hosts the local web UI in a native window
- `src/ArchHarness.App/Agents/`: agent implementations
- `src/ArchHarness.App/Core/`: orchestration/runtime contracts and flow
- `src/ArchHarness.App/Prompts/`: editable agent and orchestration prompt templates
- `src/ArchHarness.App/Copilot/`: Copilot SDK session/client integration
- `src/ArchHarness.App/Tui/`: terminal UI and screen rendering
- `src/ArchHarness.App/Storage/`: artifact and run log persistence
- `tests/ArchHarness.App.Tests/`: test project

## Prerequisites

- .NET SDK 10
- GitHub Copilot CLI installed and available on `PATH` as `copilot`
- Copilot authentication completed (the app checks this on startup)

Preflight checks run automatically at startup:

- `copilot --version` must succeed
- Copilot SDK ping/authentication must succeed

## Build

```bash
dotnet restore ArchHarness.App.sln
dotnet build ArchHarness.App.sln
```

Build tests:

```bash
dotnet build tests/ArchHarness.App.Tests/ArchHarness.App.Tests.csproj
```

## Run

Interactive mode (recommended):

```bash
dotnet run --project src/ArchHarness.Console/ArchHarness.Console.csproj
```

In interactive setup:

- `Up/Down`: move fields
- `Left/Right`: toggle workspace mode
- `Enter`: edit selected field
- `F5`: submit and start run
- `Esc`: cancel

Non-interactive mode (scriptable):

```bash
dotnet run --project src/ArchHarness.Console/ArchHarness.Console.csproj -- \
	run "Add retry logic to Copilot session creation" \
	"C:\\path\\to\\workspace" \
	"existing-folder" \
	"auto" \
	"ArchHarness.App" \
	"orchestration=gpt-5.3-codex,frontend-developer=claude-sonnet-4.6,backend-developer=gpt-5.3-codex" \
	"dotnet build \"C:\\path\\to\\workspace\\ArchHarness.App.sln\" --nologo"
```

`run` argument order:

1. `TaskPrompt`
2. `WorkspacePath`
3. `WorkspaceMode`: `existing-folder` | `new-project` | `existing-git`
4. `Workflow` (optional)
5. `ProjectName` (optional)
6. `ModelOverrides` (optional): comma-delimited `role=model`
7. `BuildCommand` (optional)

If `BuildCommand` is omitted, ArchHarness infers a suitable `dotnet build` target (`.sln`/`.csproj`) when possible.

Browser host:

```bash
dotnet run --project src/ArchHarness.Web/ArchHarness.Web.csproj
```

The web host boots the same runtime service graph, runs startup preflight, serves the browser-first control room, and exposes the local APIs used to configure runs, stream agent output, and inspect prior runs stored under `.agent-harness/runs` for a chosen workspace. In development it listens on `http://127.0.0.1:5057`.

Electron wrapper:

```bash
cd src/ArchHarness.Electron
npm install
npm start
```

The Electron wrapper starts the local `ArchHarness.Web` host if it is not already running, waits for `/api/health`, and then opens the same control-room UI in a native window.

To build the wrapper with a published local web host bundled into the app:

```bash
cd src/ArchHarness.Electron
npm install
npm run pack:mac
npm run pack:win
npm run pack:linux
```

That packaging flow first runs `dotnet publish` for `ArchHarness.Web` into `src/ArchHarness.Electron/build/web-host/`, then uses `electron-builder` to produce platform-specific artifacts under `src/ArchHarness.Electron/dist/`.

GitHub Actions release automation is defined in `.github/workflows/electron-release.yml`.

- Pushing a tag that matches `v*` builds packaged Electron releases for Windows, macOS, and Linux.
- Each matrix job bundles the published `ArchHarness.Web` host into the Electron app before packaging.
- A release job collects the generated artifacts and uploads them to a GitHub Release for the tag.
- You can also run it manually with `workflow_dispatch` and provide a `tag_name` to create or update a release.

## Configuration

Configuration is loaded from `src/ArchHarness.App/appsettings.json`.

Top-level sections:

- `agents`: default model per agent role
- `copilot`: transport, tools, timeouts, model catalog, retry settings

Prompt templates are stored under `src/ArchHarness.App/Prompts/` and copied to the build output. You can customize role instructions and orchestration templates there without editing C# source.

Example (abbreviated):

```json
{
	"agents": {
		"orchestration": { "model": "claude-sonnet-4.6" },
		"frontendDeveloper": { "model": "claude-sonnet-4.6" },
		"backendDeveloper": { "model": "gpt-5.3-codex" },
		"build": { "model": "gpt-4.1" }
	},
	"copilot": {
		"streamingResponses": true,
		"sessionAbsoluteTimeoutSeconds": 900,
		"maxRetries": 2
	}
}
```

ArchHarness no longer uses a configured model allow list. When Copilot exposes a runtime model catalog, requested models are validated against that discovered list. If discovery is unavailable, ArchHarness passes the configured model name through to Copilot without local allow-list blocking.

## Run Artifacts

Each run writes to:

`<workspace>/.agent-harness/runs/<runId>/`

Typical files:

- `events.jsonl`: timeline of run events
- `ExecutionPlan.json`: orchestrated plan
- `ArchitectureReview.json`: architecture findings/actions
- `BuildResult.json`: build execution result
- `FinalSummary.md`: end summary
- `run-log.json`: run metadata and model usage snapshot

## TUI Navigation After Run

The UI supports post-run screens for:

- run monitor
- logs
- artifacts
- review viewer
- prompts

Use the footer key hints in-app to navigate or quit.

## Troubleshooting

If startup preflight fails:

1. Run `copilot --version` and fix CLI installation issues.
2. Run `copilot`, then `/login`, and complete browser auth.
3. Retry ArchHarness.

If build validation fails:

1. Open the latest run directory under `.agent-harness/runs/`.
2. Inspect `BuildResult.json` and `events.jsonl`.
3. Re-run with an explicit `BuildCommand` if needed.

## Development Notes

- Target framework: `net10.0`
- Shared DI registration: `src/ArchHarness.App/Program.cs`
- Console entry point: `src/ArchHarness.Console/Program.cs`
- Web entry point: `src/ArchHarness.Web/Program.cs`
- Electron entry point: `src/ArchHarness.Electron/main.js`
- Main terminal flow: `src/ArchHarness.App/Tui/ChatTerminal.cs`
