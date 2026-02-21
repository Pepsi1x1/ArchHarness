# ArchHarness

Lightweight CLI harness for deterministic multi-agent orchestration against a checked-out repository.

## Run

```bash
./harness run --task "Add a new frontend filter" --repo /absolute/path/to/repo --workflow frontend_feature
```

## TUI

```bash
./archharness tui --repo /absolute/path/to/repo
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
