import json
import queue
import re
import select
import sys
import threading
import time
from dataclasses import dataclass, field
from pathlib import Path
from types import SimpleNamespace
from typing import Dict, List, Optional

from src.config.loader import apply_cli_overrides, load_config
from src.harness.orchestrator import Orchestrator

AGENT_ROLES = ["frontend", "builder", "architecture"]
MAX_DISPLAYED_LOGS = 50
INPUT_POLL_TIMEOUT = 0.2

# ---------------------------------------------------------------------------
# RunRequest – typed configuration contract produced by ConversationController
# ---------------------------------------------------------------------------

@dataclass
class RunRequest:
    """Fully resolved run configuration produced by the ConversationController."""

    workspaceMode: str  # new-project | existing-folder | existing-git
    workspacePath: str
    workflow: str
    taskPrompt: str
    projectName: Optional[str] = None
    modelOverrides: Optional[Dict[str, str]] = None
    commands: Optional[Dict[str, Optional[str]]] = None
    safety: Optional[Dict] = None

    def validate(self):
        """Raise ValueError if required fields are missing or inconsistent."""
        if not self.workspaceMode:
            raise ValueError("workspaceMode is required.")
        if not self.workspacePath:
            raise ValueError("workspacePath is required.")
        if not self.taskPrompt:
            raise ValueError("taskPrompt is required.")
        if self.workspaceMode == "new-project" and not self.projectName:
            raise ValueError("projectName is required for new-project mode.")


# ---------------------------------------------------------------------------
# ConversationController – chat-driven slot-filling and config builder
# ---------------------------------------------------------------------------

_NEW_PROJECT_RE = re.compile(r"\bnew\s+(?:\w+\s+)?(?:project|app|application)\b", re.I)
_PROJECT_NAME_RE = re.compile(r"\b(?:called|named)\s+([A-Za-z][A-Za-z0-9_\-]+)", re.I)
_ARCH_REVIEW_RE = re.compile(r"\barch(?:itecture)?\s+review\b|\breview\s+only\b", re.I)
_PATH_RE = re.compile(r"(?<!\S)((?:\.{1,2})[\\/][^\s,;]*|/[^\s,;]+|~[\\/][^\s,;]*)")
_SET_CMD_RE = re.compile(
    r"\b(?:set|change)\s+(?:the\s+)?(?P<field>[\w]+(?:\s+[\w]+)?)\s+(?:model\s+)?to\s+(?P<value>\S+)",
    re.I,
)
_USE_MODEL_RE = re.compile(
    r"\buse\s+(?P<model>\S+)\s+(?:for\s+|as\s+)?(?P<role>frontend|builder|architect(?:ure)?|tui)(?:\s+(?:model|review|agent))?",
    re.I,
)
_ROLE_TO_KEY: Dict[str, str] = {
    "frontend": "frontendModel",
    "builder": "builderModel",
    "architecture": "architectureModel",
    "architect": "architectureModel",
    "tui": "tuiAssistantModel",
}
_INLINE_EDIT_RE = re.compile(
    r"\b(?:set|change|update)\s+(?:the\s+)?(?:workspace|path|folder|project\s+name|workflow|model)\b",
    re.I,
)


class ConversationController:
    """Chat-driven configuration builder for ArchHarness runs.

    Uses pattern matching and heuristics to extract slot values from free-form
    natural-language messages.  The ``tui.assistant.model`` config key names
    the model that would drive this layer in a connected deployment; this
    implementation uses deterministic parsing so that no external API is
    required at configuration time.

    Responsibilities
    ----------------
    * Maintain conversation state (slot filling).
    * Convert chat messages into a typed ``RunRequest``.
    * Validate against rules (e.g. new-project requires a project name).
    * Persist the "draft configuration" as the user edits.
    * Provide an "explain what you will do" summary before execution.
    """

    _REQUIRED_SLOTS = ("taskPrompt", "workspacePath")

    def __init__(self, config: dict):
        self.config = config
        self._slots: Dict = {}
        self._history: List = []  # list of (role, text) tuples

    # ------------------------------------------------------------------
    # Slot-extraction helpers
    # ------------------------------------------------------------------

    @staticmethod
    def _detect_workspace_mode(path_str: str) -> str:
        """Auto-detect workspaceMode from a resolved path."""
        p = Path(path_str).resolve()
        if (p / ".git").exists():
            return "existing-git"
        return "existing-folder"

    @staticmethod
    def _extract_path(text: str) -> Optional[str]:
        """Return the first path-like token from *text*, or None."""
        m = _PATH_RE.search(text)
        return m.group(1) if m else None

    @staticmethod
    def _extract_project_name(text: str) -> Optional[str]:
        m = _PROJECT_NAME_RE.search(text)
        return m.group(1) if m else None

    @staticmethod
    def _extract_workflow(text: str) -> str:
        if _ARCH_REVIEW_RE.search(text):
            return "arch_review_only"
        return "frontend_feature"

    def _apply_model_override(self, role_kw: str, model_value: str) -> None:
        key = _ROLE_TO_KEY.get(role_kw.lower())
        if key:
            self._slots.setdefault("modelOverrides", {})[key] = model_value

    def _parse_message(self, text: str) -> None:
        """Parse *text* and update ``_slots`` in-place."""
        low = text.lower()

        # ---- inline "set/change X to Y" commands -------------------------
        m = _SET_CMD_RE.search(text)
        if m:
            field_text = m.group("field").strip().lower()
            value = m.group("value").strip()
            if any(kw in field_text for kw in ("workspace", "path", "folder")):
                resolved = str(Path(value).resolve())
                self._slots["workspacePath"] = resolved
                if "workspaceMode" not in self._slots:
                    self._slots["workspaceMode"] = self._detect_workspace_mode(resolved)
                return
            if "project" in field_text and "name" in field_text:
                self._slots["projectName"] = value
                return
            if "workflow" in field_text:
                self._slots["workflow"] = value
                return
            for role_kw in _ROLE_TO_KEY:
                if role_kw in field_text:
                    self._apply_model_override(role_kw, value)
                    return

        # ---- "use <model> for/as <role>" model overrides ------------------
        for mo in _USE_MODEL_RE.finditer(text):
            self._apply_model_override(mo.group("role").lower(), mo.group("model"))

        # ---- workflow -----------------------------------------------------
        if "workflow" not in self._slots:
            self._slots["workflow"] = self._extract_workflow(text)

        # ---- new-project intent ------------------------------------------
        if _NEW_PROJECT_RE.search(text):
            self._slots["workspaceMode"] = "new-project"
            name = self._extract_project_name(text)
            if name:
                self._slots["projectName"] = name

        # ---- path detection ----------------------------------------------
        raw_path = self._extract_path(text)
        if raw_path and not self._slots.get("workspacePath"):
            resolved = str(Path(raw_path).resolve())
            self._slots["workspacePath"] = resolved
            if "workspaceMode" not in self._slots:
                self._slots["workspaceMode"] = self._detect_workspace_mode(resolved)

        # ---- task prompt -------------------------------------------------
        if "taskPrompt" not in self._slots:
            # Strip path tokens for a cleaner task description.
            task = _PATH_RE.sub("", text).strip()
            if task:
                self._slots["taskPrompt"] = task
        elif not _INLINE_EDIT_RE.search(text) and not _USE_MODEL_RE.search(text):
            # Pure free-form message after task is set → treat as task update.
            self._slots["taskPrompt"] = text.strip()

    def _missing_slots(self) -> List[str]:
        missing = []
        if not self._slots.get("taskPrompt"):
            missing.append("taskPrompt")
        if not self._slots.get("workspacePath"):
            missing.append("workspacePath")
        if self._slots.get("workspaceMode") == "new-project" and not self._slots.get("projectName"):
            missing.append("projectName")
        return missing

    def _clarify(self, missing: List[str]) -> str:
        questions = {
            "taskPrompt": "What would you like the agents to do? Please describe the task.",
            "workspacePath": (
                "Which folder should I use? Please provide a path "
                "(e.g. ./my-project or /absolute/path)."
            ),
            "projectName": "What should the new project be called?",
        }
        return questions.get(missing[0], f"Please provide: {missing[0]}")

    # ------------------------------------------------------------------
    # Public API
    # ------------------------------------------------------------------

    def process_message(self, text: str):
        """Consume *text*, update state, and return ``(response, is_complete)``."""
        self._history.append(("user", text))
        self._parse_message(text)
        missing = self._missing_slots()
        if missing:
            response = self._clarify(missing)
            self._history.append(("assistant", response))
            return response, False
        response = self.summary()
        self._history.append(("assistant", response))
        return response, True

    def update_slot(self, key: str, value) -> None:
        """Directly update a draft slot (for programmatic inline edits)."""
        self._slots[key] = value
        if key == "workspacePath" and "workspaceMode" not in self._slots:
            self._slots["workspaceMode"] = self._detect_workspace_mode(value)

    def summary(self) -> str:
        """Return a human-readable run summary (the confirmation step)."""
        mode = self._slots.get("workspaceMode", "existing-folder")
        init_git = (self._slots.get("safety") or {}).get("initGit", mode == "new-project")
        lines = [
            "┌─ Run Summary ─────────────────────────────────────────",
            f"│  Task:           {self._slots.get('taskPrompt', '—')}",
            f"│  Workspace mode: {mode}",
            f"│  Path:           {self._slots.get('workspacePath', '—')}",
        ]
        if self._slots.get("projectName"):
            lines.append(f"│  Project name:   {self._slots['projectName']}")
        lines.append(f"│  Workflow:       {self._slots.get('workflow', 'frontend_feature')}")
        overrides = self._slots.get("modelOverrides") or {}
        for k, v in overrides.items():
            lines.append(f"│  {k:<18} {v}")
        assistant_model = self.config.get("tui", {}).get("assistant", {}).get("model", "gpt-5-mini")
        lines.append(f"│  TUI assistant:  {assistant_model}")
        lines.append(f"│  Write scope:    {self._slots.get('workspacePath', '—')}")
        lines.append(f"│  Init git:       {init_git}")
        lines.append("└───────────────────────────────────────────────────────")
        lines.append("\nType 'run' to start, or describe what to change.")
        return "\n".join(lines)

    def build_run_request(self) -> RunRequest:
        """Build and return a validated :class:`RunRequest` from current draft state."""
        mode = self._slots.get("workspaceMode", "existing-folder")
        path = self._slots.get("workspacePath", ".")
        safety = self._slots.get("safety") or {
            "writeScopeRoot": path,
            "initGit": mode == "new-project",
        }
        req = RunRequest(
            workspaceMode=mode,
            workspacePath=path,
            workflow=self._slots.get("workflow", "frontend_feature"),
            taskPrompt=self._slots.get("taskPrompt", ""),
            projectName=self._slots.get("projectName"),
            modelOverrides=self._slots.get("modelOverrides"),
            commands=self._slots.get("commands"),
            safety=safety,
        )
        req.validate()
        return req


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


def _monitor_run(orchestrator, task, workspace_path, workflow, workspace_mode="existing-git", project_name=None, init_git=None):
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
                workspace_path,
                workflow,
                workspace_mode=workspace_mode,
                project_name=project_name,
                init_git=init_git,
                event_sink=on_event,
                control=control,
            )
        except Exception as exc:  # pragma: no cover - surfaced to user
            result["error"] = f"{type(exc).__name__}: {exc}"

    thread = threading.Thread(target=_runner, daemon=True)
    thread.start()
    print("\nRun Monitor (controls: p pause/resume, c cancel)")
    print(f"Workspace type: {workspace_mode}")
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


def _chat_new_run(config):
    """Interactive chat-driven run configuration.

    Guides the user through a conversation to build a :class:`RunRequest` that
    the orchestrator can execute.  Returns the completed ``RunRequest``, or
    ``None`` if the user cancels.
    """
    assistant_cfg = config.get("tui", {}).get("assistant", {})
    model_name = assistant_cfg.get("model", "gpt-5-mini")
    controller = ConversationController(config)

    print(f"\nArchHarness Chat  [TUI assistant: {model_name}]")
    print("Describe what you want to do.  Examples:")
    print("  • Create a new React app called ClaimsPortal and add a login page.")
    print("  • Use my existing folder ./MyApp and implement the feature.")
    print("  • Use Opus for architecture review of ./my-repo")
    print("Quick actions: [N]ew project  [E]xisting folder  [R]eview diff  [C]ancel\n")

    while True:
        try:
            raw = input("You: ").strip()
        except EOFError:
            return None

        if not raw:
            continue

        low = raw.lower()
        if low in ("cancel", "c", "q", "quit"):
            return None

        # Quick-action shortcuts
        if low in ("n", "new project"):
            raw = "Create a new project"
        elif low in ("e", "existing folder"):
            raw = "Use an existing folder"
        elif low in ("r", "review diff"):
            raw = "Architecture review only"

        response, complete = controller.process_message(raw)
        print(f"\nAssistant:\n{response}\n")

        if complete:
            try:
                confirm = input("[R]un / [E]dit / [C]ancel: ").strip().lower()
            except EOFError:
                return None
            if confirm in ("r", "run", ""):
                try:
                    return controller.build_run_request()
                except ValueError as exc:
                    print(f"Configuration error: {exc}\n")
                    continue
            elif confirm in ("e", "edit"):
                print("Describe what to change (e.g. 'set workspace to ./other'):\n")
                continue
            else:
                return None


def run_tui(repo_path=None, config_path=None):
    """Launch the TUI.

    When *repo_path* is provided the run list for that workspace is displayed
    alongside the usual commands.  When omitted (``archharness tui`` with no
    ``--path``), the chat-driven interface is offered immediately so the user
    can configure and start a run without supplying any CLI parameters.
    """
    if repo_path:
        repo_path = str(Path(repo_path).resolve())
    while True:
        if repo_path:
            runs = _print_run_list(repo_path)
            print("\nCommands: n(new chat run), a <n>(artifacts), l <n>(logs), q(quit)")
        else:
            runs = []
            print("\nNo workspace specified.")
            print("Commands: n(new chat run), q(quit)")
        try:
            raw = input("> ").strip()
        except EOFError:
            return
        if raw == "q":
            return
        if raw == "n":
            config = load_config(config_path)
            run_request = _chat_new_run(config)
            if run_request is None:
                continue
            overrides = SimpleNamespace(
                frontend_model=(run_request.modelOverrides or {}).get("frontendModel"),
                architecture_model=(run_request.modelOverrides or {}).get("architectureModel"),
                builder_model=(run_request.modelOverrides or {}).get("builderModel"),
                max_iterations=None,
                output_mode=None,
            )
            config = apply_cli_overrides(config, overrides)
            orchestrator = Orchestrator(config)
            init_git = (run_request.safety or {}).get("initGit")
            run_dir = _monitor_run(
                orchestrator,
                run_request.taskPrompt,
                run_request.workspacePath,
                run_request.workflow,
                workspace_mode=run_request.workspaceMode,
                project_name=run_request.projectName,
                init_git=init_git,
            )
            print(f"\nRun completed: {run_dir}")
            if not repo_path:
                repo_path = run_request.workspacePath
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
