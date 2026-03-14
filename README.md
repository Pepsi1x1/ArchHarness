# ArchHarness

ArchHarness is a .NET application suite that runs a multi-agent software workflow on top of GitHub Copilot SDK sessions.

It now ships with a shared runtime library, a console host for the existing terminal workflow, and a cross-platform desktop host. The console host preserves the existing terminal-first workflow, while the desktop host provides a reference-inspired shell for configuring runs, streaming live progress, inspecting agent output, and reviewing persisted artefacts.

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
- `src/ArchHarness.Desktop/`: Avalonia desktop shell and host-specific UI services
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

Desktop shell:

```bash
dotnet run --project src/ArchHarness.Desktop/ArchHarness.Desktop.csproj
```

The desktop host boots the same runtime service graph, runs startup preflight, allows you to configure and launch orchestrated runs, streams runtime progress and agent output, and lets you inspect prior runs stored under `.agent-harness/runs` for a chosen workspace.

Electron wrapper:

```bash
cd src/ArchHarness.Electron
npm install
npm start
```

The Electron wrapper starts the local `ArchHarness.Web` host if it is not already running, waits for `/api/health`, and then opens the control-room UI in a native window.

To build the wrapper with a published local web host bundled into the app:

```bash
cd src/ArchHarness.Electron
npm install
npm run pack:mac
```

That packaging flow first runs `dotnet publish` for `ArchHarness.Web` into `src/ArchHarness.Electron/build/web-host/`, then uses `electron-builder` to produce a macOS zip bundle under `src/ArchHarness.Electron/dist/`.

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
- Desktop entry point: `src/ArchHarness.Desktop/Program.cs`
- Main terminal flow: `src/ArchHarness.App/Tui/ChatTerminal.cs`
