import json
import queue
import select
import sys
import threading
import time
from pathlib import Path
from types import SimpleNamespace

from src.config.loader import apply_cli_overrides, load_config
from src.harness.orchestrator import Orchestrator

AGENT_ROLES = ["frontend", "builder", "architecture"]
MAX_DISPLAYED_LOGS = 50
INPUT_POLL_TIMEOUT = 0.2


class RunControl:
    def __init__(self):
        self._paused = threading.Event()
        self._cancelled = threading.Event()

    def toggle_pause(self):
        if self._paused.is_set():
            self._paused.clear()
        else:
            self._paused.set()

    def cancel(self):
        self._cancelled.set()

    def is_cancelled(self):
        return self._cancelled.is_set()

    def wait_if_paused(self):
        while self._paused.is_set() and not self._cancelled.is_set():
            time.sleep(0.1)


def _runs_root(repo_path):
    return Path(repo_path) / ".agent-harness" / "runs"


def _read_events(run_dir):
    events_path = Path(run_dir) / "events.jsonl"
    if not events_path.exists():
        return []
    events = []
    for line in events_path.read_text().splitlines():
        if line.strip():
            events.append(json.loads(line))
    return events


def _show_artifacts(run_dir):
    files = sorted(p.name for p in Path(run_dir).iterdir() if p.is_file())
    print(f"\nArtifacts in {run_dir}:")
    for name in files:
        print(f" - {name}")


def _show_logs(run_dir):
    agent = input("Filter agent role (frontend|builder|architecture or blank): ").strip().lower()
    term = input("Search term (blank for all): ").strip().lower()
    matched = []
    for event in _read_events(run_dir):
        role = (event.get("agentRole") or "").lower()
        text = json.dumps(event).lower()
        if agent and role != agent:
            continue
        if term and term not in text:
            continue
        matched.append(event)
    print(f"\nLogs ({len(matched)} event(s))")
    for event in matched[-MAX_DISPLAYED_LOGS:]:
        role = event.get("agentRole") or "-"
        print(f"[{event['timestamp']}] {event['level']} {event['source']} {role} {event['message']}")


def _print_run_list(repo_path):
    root = _runs_root(repo_path)
    runs = sorted([p for p in root.iterdir() if p.is_dir()], reverse=True) if root.exists() else []
    print("\nRun List")
    if not runs:
        print(" (no runs yet)")
    for idx, run in enumerate(runs, start=1):
        print(f" {idx}. {run.name}")
    return runs


def _print_agent_status(agent_state):
    print("\nAgents")
    for role in AGENT_ROLES:
        state = agent_state.get(role, {})
        print(
            f" - {role:<12} model={state.get('model', '-'):<10} status={state.get('status', 'Idle'):<9} "
            f"step={state.get('currentStep', '-')}"
        )


def _monitor_run(orchestrator, task, repo_path, workflow):
    event_queue = queue.Queue()
    control = RunControl()
    result = {"run_dir": None, "error": None}
    agent_state = {}

    def on_event(event):
        event_queue.put(event)
        role = event.get("agentRole")
        if role and event.get("type") in {"agent.status", "agent.step"}:
            data = event.get("data") or {}
            agent_state.setdefault(role, {}).update(data)
            if "model" not in agent_state[role]:
                for agent in AGENT_ROLES:
                    if agent == role:
                        agent_state[role]["model"] = getattr(orchestrator, agent).model

    def _runner():
        try:
            result["run_dir"] = orchestrator.run(
                task,
                repo_path,
                workflow,
                event_sink=on_event,
                control=control,
            )
        except Exception as exc:  # pragma: no cover - surfaced to user
            result["error"] = f"{type(exc).__name__}: {exc}"

    thread = threading.Thread(target=_runner, daemon=True)
    thread.start()
    print("\nRun Monitor (controls: p pause/resume, c cancel)")
    while thread.is_alive():
        saw_event = False
        while not event_queue.empty():
            event = event_queue.get()
            print(f"[{event['timestamp']}] {event['source']}:{event.get('agentRole') or '-'} {event['message']}")
            saw_event = True
        if saw_event:
            _print_agent_status(agent_state)
        if select.select([sys.stdin], [], [], INPUT_POLL_TIMEOUT)[0]:
            command = sys.stdin.readline().strip().lower()
            if command == "p":
                control.toggle_pause()
            elif command == "c":
                control.cancel()
    thread.join()
    if result["error"]:
        raise RuntimeError(result["error"])
    return result["run_dir"]


def run_tui(repo_path, config_path=None):
    repo_path = str(Path(repo_path).resolve())
    while True:
        runs = _print_run_list(repo_path)
        print("\nCommands: n(new run), a <n>(artifacts), l <n>(logs), q(quit)")
        try:
            raw = input("> ").strip()
        except EOFError:
            return
        if raw == "q":
            return
        if raw == "n":
            try:
                task = input("Task prompt: ").strip()
                workflow = input("Workflow [frontend_feature|arch_review_only]: ").strip() or "frontend_feature"
                frontend_model = input("Frontend model override (optional): ").strip() or None
                architecture_model = input("Architecture model override (optional): ").strip() or None
                builder_model = input("Builder model override (optional): ").strip() or None
            except EOFError:
                return
            overrides = SimpleNamespace(
                frontend_model=frontend_model,
                architecture_model=architecture_model,
                builder_model=builder_model,
                max_iterations=None,
                output_mode=None,
            )
            config = apply_cli_overrides(load_config(config_path), overrides)
            orchestrator = Orchestrator(config)
            run_dir = _monitor_run(orchestrator, task, repo_path, workflow)
            print(f"\nRun completed: {run_dir}")
            continue

        if raw.startswith(("a ", "l ")):
            parts = raw.split()
            if len(parts) != 2 or not parts[1].isdigit():
                print("Invalid command. Use a <n> or l <n>.")
                continue
            idx = int(parts[1]) - 1
            if idx < 0 or idx >= len(runs):
                print("Run index out of range.")
                continue
            run_dir = runs[idx]
            if parts[0] == "a":
                _show_artifacts(run_dir)
            else:
                _show_logs(run_dir)
            continue

        print("Unknown command.")
