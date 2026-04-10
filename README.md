# ArchHarness

A multi-agent software engineering harness built on the GitHub Copilot SDK. ArchHarness orchestrates specialized AI agents to plan, implement, review, and validate code changes across a target workspace.

Three hosts share the same runtime library:

- **Console** — interactive terminal UI with scriptable CLI mode
- **Web** — local ASP.NET Core host serving a browser control room
- **Electron** — native desktop wrapper around the web host

## What It Does

1. Accepts a task prompt and workspace target.
2. Builds an execution plan via orchestration and planning agents.
3. Delegates work to specialized agents:
   - **Orchestration** — plan creation, step coordination, completion validation
   - **Planning** — dedicated reasoning-heavy plan refinement and clarification
   - **FrontendDeveloper / BackendDeveloper** — implementation changes
   - **Build** — build execution, result triage, and fix suggestions
   - **Architecture** — SOLID principle enforcement via static analyzers (SRP, DIP, ISP, OCP/LSP, DRY, completeness, missing tests)
   - **Security** — OWASP-oriented review plus heuristic analysis (hardcoded secrets, insecure transport, SQL injection, XSS, TLS bypass)
   - **CodingStyle** — naming conventions and language-specific style enforcement with direct file editing
4. Runs review loops (architecture, security, style) until findings are resolved or max iterations reached.
5. Validates builds and records final status.
6. Persists run artifacts under `.agent-harness/runs/<runId>/` in the target workspace.

## Workflows

| Name | Description |
| --- | --- |
| `auto` | Default orchestrator-driven workflow |
| `planning` | Clarification and plan approval only (no execution) |
| `architecture-loop` | Architecture review remediation loop |
| `frontend-feature` | Legacy frontend-focused default |

## Repository Layout

```
src/
  ArchHarness.App/          Shared runtime library
    Agents/                  Agent implementations
      Analyzers/             Static architecture analyzers (SRP, DIP, ISP, etc.)
    Copilot/                 Copilot SDK session, client, governance, error handling
    Core/                    Orchestration runtime, plan execution, validation
    Guidelines/              Language-specific review guidelines (.NET, Vue 3)
    Prompts/                 Editable agent and orchestration prompt templates
    SourceControl/           Git, GitHub OAuth, Azure DevOps integration
    Storage/                 Artifact and run log persistence
    Tui/                     Terminal UI and screen rendering
    Workspace/               File-system and git workspace adapters
  ArchHarness.Console/       Console entry point
  ArchHarness.Web/           ASP.NET Core web host and API
  ArchHarness.Electron/      Electron desktop wrapper
tests/
  ArchHarness.App.Tests/     Unit and integration tests
```

## Prerequisites

- .NET SDK 10
- GitHub Copilot CLI available on `PATH` as `copilot`, authenticated

Preflight checks run automatically at startup and verify `copilot` availability and authentication state. Git operations use LibGit2Sharp and do not require the git CLI.

## Build

```bash
dotnet restore ArchHarness.App.sln
dotnet build ArchHarness.App.sln
```

Run tests:

```bash
dotnet test tests/ArchHarness.App.Tests/ArchHarness.App.Tests.csproj
```

## Run

### Console (interactive)

```bash
dotnet run --project src/ArchHarness.Console/ArchHarness.Console.csproj
```

- `Up/Down` — move fields
- `Left/Right` — toggle workspace mode
- `Enter` — edit selected field
- `F5` — submit and start run
- `Esc` — cancel

### Console (scriptable)

```bash
dotnet run --project src/ArchHarness.Console/ArchHarness.Console.csproj -- \
  run "Add retry logic to session creation" \
  "C:\path\to\workspace" \
  "existing-git" \
  "auto" \
  "MyProject" \
  "orchestration=claude-opus-4.6,backend-developer=gpt-5.4" \
  "dotnet build MyProject.sln --nologo"
```

Argument order: `TaskPrompt`, `WorkspacePath`, `WorkspaceMode` (`existing-folder` | `new-project` | `existing-git`), `Workflow`, `ProjectName`, `ModelOverrides` (comma-delimited `role=model`), `BuildCommand`. From `Workflow` onward all arguments are optional. If `BuildCommand` is omitted, ArchHarness infers a suitable `dotnet build` target when possible.

### Web host

```bash
dotnet run --project src/ArchHarness.Web/ArchHarness.Web.csproj
```

Listens on `http://127.0.0.1:5057` (loopback only). Serves the browser control room and exposes REST APIs for project management, run lifecycle, agent streaming, settings, model listing, source control providers (including GitHub OAuth device flow and Azure DevOps), and run history.

### Electron

```bash
cd src/ArchHarness.Electron
npm install
npm start          # development
npm run dev        # development with devtools
```

The Electron wrapper starts the local web host if needed, waits for `/api/health`, and opens the control room in a native window.

#### Packaging

```bash
npm run pack:win   # Windows (NSIS + ZIP)
npm run pack:mac   # macOS (ZIP)
npm run pack:linux # Linux (AppImage + ZIP)
```

Runs `dotnet publish` for `ArchHarness.Web` into `build/web-host/`, then `electron-builder` produces platform artifacts under `dist/`.

Release automation: `.github/workflows/electron-release.yml` builds and publishes packaged releases for all platforms on `v*` tags (or via `workflow_dispatch`).

## Configuration

Configuration is loaded from `src/ArchHarness.App/appsettings.json`.

| Section | Purpose |
| --- | --- |
| `agents` | Default model, reasoning effort, and review loop settings per agent role |
| `agents.architecture.analyzers` | Which static analyzers to run (SRP, DIP, ISP, OCP/LSP, DRY, completeness, missing tests) |
| `agents.security.analyzers` | Which security heuristics to enable (hardcoded secrets, insecure transport, SQL injection, XSS, TLS bypass) |
| `copilot` | Transport mode, conversation model, session timeouts, prompt/completion limits, retry settings |

Example (abbreviated):

```json
{
  "agents": {
    "orchestration": { "model": "claude-opus-4.6" },
    "planning": { "model": "gpt-5.4", "reasoningEffort": "xhigh" },
    "frontendDeveloper": { "model": "claude-sonnet-4.6" },
    "backendDeveloper": { "model": "gpt-5.4" },
    "build": { "model": "gpt-4.1" },
    "codingStyle": { "model": "gpt-5.4" },
    "security": { "model": "gpt-5.4" },
    "architecture": { "model": "claude-opus-4.6" }
  },
  "copilot": {
    "conversationModel": "gpt-5-mini",
    "useStdio": true,
    "streamingResponses": true,
    "sessionAbsoluteTimeoutSeconds": 900,
    "maxRetries": 2
  }
}
```

Models are validated against Copilot's runtime model catalog when available. If discovery is unavailable, configured model names are passed through without local blocking.

Prompt templates under `src/ArchHarness.App/Prompts/` and review guidelines under `src/ArchHarness.App/Guidelines/` (`.NET` and `Vue 3` variants for architecture, security, and style) can be customized without editing C# source.

## Run Artifacts

Each run writes to `<workspace>/.agent-harness/runs/<runId>/`:

| File | Contents |
| --- | --- |
| `events.jsonl` | Timeline of run events |
| `ExecutionPlan.json` | Orchestrated plan |
| `ArchitectureReview.json` | Architecture findings and actions |
| `BuildResult.json` | Build execution result |
| `FinalSummary.md` | End summary |
| `run-log.json` | Run metadata and model usage snapshot |

## Troubleshooting

**Startup preflight fails:**

1. Verify `copilot --version` succeeds.
2. Run `copilot` then `/login` to complete browser auth.

**Build validation fails:**

1. Inspect `BuildResult.json` and `events.jsonl` in the run directory.
2. Re-run with an explicit `BuildCommand`.

## Development

| | Path |
| --- | --- |
| Target framework | `net10.0` |
| Shared DI registration | `src/ArchHarness.App/Program.cs` |
| Console entry point | `src/ArchHarness.Console/Program.cs` |
| Web entry point | `src/ArchHarness.Web/Program.cs` |
| Electron entry point | `src/ArchHarness.Electron/main.js` |
| SDK integration | `src/ArchHarness.App/Copilot/` |
| Key dependency | `GitHub.Copilot.SDK 0.2.1` |
