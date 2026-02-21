# ArchHarness

Lightweight CLI harness for deterministic multi-agent orchestration against a checked-out repository.

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
