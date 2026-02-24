# ArchHarness

ArchHarness is a .NET console application that runs a multi-agent software workflow on top of GitHub Copilot SDK sessions.

It provides a terminal UI for setup and monitoring, delegates work to specialized agents, runs build verification, and writes full run artifacts for review.

## What It Does

- Accepts a task prompt and workspace target.
- Builds an execution plan with an orchestration agent.
- Delegates implementation/review steps to specialized agents:
	- `Orchestration`: planning and completion validation
	- `Frontend`: frontend planning
	- `Builder`: implementation changes
	- `Architecture`: architecture enforcement and findings
- Optionally loops architecture remediation until high-severity findings are cleared (or max iterations reached).
- Runs build validation and records final status.
- Persists run artifacts under `.agent-harness/runs/<runId>/` in the target workspace.

## Repository Layout

- `src/ArchHarness.App/`: main application
- `src/ArchHarness.App/Agents/`: agent implementations
- `src/ArchHarness.App/Core/`: orchestration/runtime contracts and flow
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
dotnet run --project src/ArchHarness.App/ArchHarness.App.csproj
```

In interactive setup:

- `Up/Down`: move fields
- `Left/Right`: toggle workspace mode
- `Enter`: edit selected field
- `F5`: submit and start run
- `Esc`: cancel

Non-interactive mode (scriptable):

```bash
dotnet run --project src/ArchHarness.App/ArchHarness.App.csproj -- \
	run "Add retry logic to Copilot session creation" \
	"C:\\path\\to\\workspace" \
	"existing-folder" \
	"auto" \
	"ArchHarness.App" \
	"orchestration=gpt-5.3-codex,builder=gpt-5.3-codex" \
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

## Configuration

Configuration is loaded from `src/ArchHarness.App/appsettings.json`.

Top-level sections:

- `agents`: default model per agent role
- `copilot`: transport, tools, timeouts, model catalog, retry settings

Example (abbreviated):

```json
{
	"agents": {
		"orchestration": { "model": "claude-sonnet-4.6" },
		"builder": { "model": "gpt-5.3-codex" }
	},
	"copilot": {
		"streamingResponses": true,
		"sessionAbsoluteTimeoutSeconds": 900,
		"supportedModels": ["gpt-5.3-codex", "claude-sonnet-4.6"]
	}
}
```

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
- DI entry point: `src/ArchHarness.App/Program.cs`
- Main terminal flow: `src/ArchHarness.App/Tui/ChatTerminal.cs`
