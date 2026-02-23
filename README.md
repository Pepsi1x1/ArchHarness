# ArchHarness

ArchHarness vNext is a .NET 10 single-application console host with a chat-driven TUI and explicit multi-agent orchestration.

## vNext Run

```bash
./archharness
```

or:

```bash
dotnet run --project ./src/ArchHarness.App/ArchHarness.App.csproj
```

The vNext app wires four agents (Orchestration, Frontend, Builder, Architecture) through a single Copilot client abstraction and persists run artefacts under `.agent-harness/runs/<timestamp>/`.

## Legacy Python CLI

The previous Python harness command remains available:

## Run

```bash
./harness run --task "Add a new frontend filter" --mode existing-git --path /absolute/path/to/repo --workflow frontend_feature
```

Workspace modes:

- `existing-git` (default): requires `.git` in `--path` (or legacy `--repo`)
- `existing-folder`: works without Git; optional `--init-git true|false`
- `new-project`: creates `<path>/<project-name>`; requires `--project-name`; Git init defaults to enabled unless `--init-git false`

## TUI

```bash
./archharness tui --path /absolute/path/to/folder
```

The TUI provides a run list, interactive run monitor with pause/cancel controls, searchable logs, and per-run artifact browsing.

## Config

Provide JSON or YAML config. Default models:

- `agents.frontend.model`: `sonnet-4.6`
- `agents.architecture.model`: `opus-4.6`
- `agents.builder.model`: `codex-5.3`

CLI flags override config values:

- `--frontend-model`
- `--architecture-model`
- `--builder-model`

Run artifacts are stored at `.agent-harness/runs/<timestamp>/`.
