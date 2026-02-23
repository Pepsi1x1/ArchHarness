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
