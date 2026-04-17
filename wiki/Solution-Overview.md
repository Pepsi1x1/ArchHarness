# Solution Overview

## Relevant files

| Path | Why it matters |
| --- | --- |
| `ArchHarness.App.sln` | Defines the .NET solution and shows which projects are built by standard `dotnet` commands. |
| `src\ArchHarness.App\ArchHarness.App.csproj` | Declares the shared runtime library, target framework, and key package references. |
| `src\ArchHarness.Console\Program.cs` | Console composition root. |
| `src\ArchHarness.Web\Program.cs` | Web host composition root. |
| `src\ArchHarness.Electron\package.json` | Electron packaging scripts and desktop metadata. |

ArchHarness is a local software-engineering harness built around the GitHub Copilot SDK. The repository centers on a shared .NET 10 runtime that plans work, delegates implementation to specialized agents, runs architecture/security/style review passes, validates outcomes, and can generate wiki documentation across multiple repositories.

## Repository shape

| Path | Purpose |
| --- | --- |
| `src\ArchHarness.App\` | Shared runtime library: orchestration, agents, Copilot integration, storage, source control, prompts, and guidelines. |
| `src\ArchHarness.Console\` | Terminal host and scriptable CLI entry point. |
| `src\ArchHarness.Web\` | ASP.NET Core web host, REST API, browser UI, and SSE event stream. |
| `src\ArchHarness.Electron\` | Electron desktop wrapper and packaging scripts. |
| `tests\ArchHarness.App.Tests\` | xUnit-based unit and integration test suite. |
| `.github\workflows\electron-release.yml` | Cross-platform Electron packaging and GitHub release automation. |
| `wiki\` | Repository wiki output folder. |

## Project inventory

| Project | Type | In `ArchHarness.App.sln` | Notes |
| --- | --- | --- | --- |
| `ArchHarness.App` | .NET class library (`net10.0`) | Yes | Holds almost all business logic and references `GitHub.Copilot.SDK 0.2.2`, `LibGit2Sharp 0.31.0`, Roslyn, hosting, and DPAPI support. |
| `ArchHarness.Console` | .NET console app (`net10.0`) | Yes | Thin composition root that hosts the TUI and CLI parser. |
| `ArchHarness.Web` | ASP.NET Core app (`net10.0`) | Yes | Local control plane, browser UI, Markdown rendering, and active-run streaming. |
| `ArchHarness.Electron` | Node/Electron app | No | Built separately with `npm`; wraps the local web host and packages desktop releases. |
| `ArchHarness.App.Tests` | xUnit test project (`net10.0`) | Yes | References both `ArchHarness.App` and `ArchHarness.Web` for runtime and web-host tests. |

The Electron wrapper is part of the repository but not part of the `.sln`, so `dotnet build ArchHarness.App.sln` does not produce desktop packages.

## Shared runtime map

| Area | Contents |
| --- | --- |
| `Agents\` | Concrete agent implementations such as Orchestration, Planning, FrontendDeveloper, BackendDeveloper, Build, CodingStyle, Security, Architecture, and WikiDoc. |
| `Agents\Analyzers\` | Static rule helpers used by architecture and security reviews. |
| `Copilot\` | Copilot client/session plumbing, model discovery, governance hooks, retry behavior, and usage logging. |
| `Core\` | Run contracts, orchestration pipeline, plan parsing, review loop, verification workflow, CLI parsing, and WikiDoc workflow. |
| `SourceControl\` | Provider connection management, GitHub OAuth device flow, Azure DevOps/GitHub review providers, and LibGit2Sharp workspace operations. |
| `Storage\` | User-scoped catalogs, run history, artifact writers, and resumable run-state persistence. |
| `Tui\` | Console screen rendering, navigation, setup flow, and run monitoring. |
| `Workspace\` | File-system and Git-backed workspace adapters. |
| `Prompts\` | Markdown prompt templates loaded from disk at runtime. |
| `Guidelines\` | Markdown guidance files for developer and review agents. |

All .NET hosts copy or link `appsettings.json`, `Prompts\**\*.md`, and `Guidelines\**\*.md` into their output directories so prompt and guideline changes can ship without recompiling the runtime library.

## Workflow catalog

The stable workflow identifiers come from `src\ArchHarness.App\Constants\WorkflowNames.cs` and are surfaced through `WorkflowCatalog`:

| Workflow ID | Purpose | Suggested CLI shape |
| --- | --- | --- |
| `auto` | Default orchestrator-driven workflow. | `run <taskPrompt> <workspacePath> <workspaceMode> auto` |
| `planning` | Clarification and plan approval only; no implementation execution. | `run <taskPrompt> <workspacePath> <workspaceMode> planning` |
| `architecture-loop` | Iterative architecture, security, and style remediation loop. | `run <workspacePath> <workspaceMode> architecture-loop` |
| `wikidoc` | Recursive Git repository documentation with megawiki synthesis. | `wikidoc <scanRoot> [projectName] [modelOverrides]` |
| `frontend_feature` | Legacy default workflow ID when no explicit workflow is supplied. | `run <taskPrompt> <workspacePath> <workspaceMode> frontend_feature` |

Automation should use the underscore form `frontend_feature`. That is the committed constant in code, even though the README's workflow table uses the more human-readable `frontend-feature` label.

## Runtime and packaging choices that affect operations

1. `ArchHarness.App.csproj` sets `CopilotSkipCliDownload=true`, so the repository expects a working Copilot CLI in the local environment instead of downloading one during build.
2. Git operations are handled in-process through LibGit2Sharp rather than by shelling out to the `git` CLI.
3. The browser host is intentionally local only. `appsettings.json` defaults to `http://127.0.0.1:5057`, and the web host rejects non-loopback clients.
4. Wiki documentation is a first-class workflow inside the runtime, not a separate utility script.

## See also

- [Architecture and Execution Flow](Architecture-and-Execution-Flow.md)
- [Hosts and User Interfaces](Hosts-and-User-Interfaces.md)
- [Configuration and Models](Configuration-and-Models.md)
