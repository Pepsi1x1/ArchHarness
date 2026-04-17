# ArchHarness

ArchHarness is a local software-engineering harness built on the GitHub Copilot SDK. The repository combines a shared .NET runtime, console and web hosts, an Electron desktop wrapper, workspace-local run persistence, and a dedicated WikiDoc workflow for repository documentation generation.

## Table of contents

| Page | Purpose |
| --- | --- |
| [Solution Overview](Solution-Overview.md) | Repository layout, projects, workflows, and major subsystems. |
| [Architecture and Execution Flow](Architecture-and-Execution-Flow.md) | Run lifecycle, orchestration pipeline, review loops, and verification behavior. |
| [Hosts and User Interfaces](Hosts-and-User-Interfaces.md) | Console, web, browser, and Electron entry points and interaction patterns. |
| [WikiDoc Workflow](WikiDoc-Workflow.md) | Repository discovery, output rules, fallback behavior, and megawiki synthesis. |
| [Source Control and Providers](Source-Control-and-Providers.md) | Git operations, provider connections, GitHub OAuth, and Azure DevOps integration. |
| [Storage, Run Artifacts, and State](Storage-Run-Artifacts-and-State.md) | User-scoped catalogs, workspace-local run outputs, and resumable checkpoints. |
| [Configuration and Models](Configuration-and-Models.md) | `appsettings.json`, model resolution, prompt assets, and review guidelines. |
| [Development and Testing](Development-and-Testing.md) | Local prerequisites, build/run/package commands, test coverage, and release automation. |
