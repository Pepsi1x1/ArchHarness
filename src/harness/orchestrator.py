import hashlib
import json
import re
from datetime import datetime, timezone
from pathlib import Path

from src.adapters.repo.adapter import RepoAdapter
from src.agents.roles import ArchitectureAgent, BuilderAgent, FrontendAgent


class Orchestrator:
    def __init__(self, config):
        self.config = config
        self.frontend = FrontendAgent(config["agents"]["frontend"]["model"])
        self.builder = BuilderAgent(config["agents"]["builder"]["model"])
        self.architecture = ArchitectureAgent(config["agents"]["architecture"]["model"])

    def _run_dir(self, repo_path):
        stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
        run_dir = Path(repo_path) / ".agent-harness" / "runs" / stamp
        run_dir.mkdir(parents=True, exist_ok=True)
        return run_dir

    def _write_json(self, path, payload):
        path.write_text(json.dumps(payload, indent=2))

    def _redact(self, value):
        if isinstance(value, str):
            return re.sub(r"(ghp_[A-Za-z0-9]{20,}|sk-[A-Za-z0-9]{20,}|AKIA[0-9A-Z]{16})", "***REDACTED***", value)
        if isinstance(value, list):
            return [self._redact(v) for v in value]
        if isinstance(value, dict):
            return {k: self._redact(v) for k, v in value.items()}
        return value

    def _emit_event(self, run_dir, source, event_type, level, message, data=None, agent_role=None, event_sink=None):
        event = {
            "runId": Path(run_dir).name,
            "timestamp": datetime.now(timezone.utc).isoformat(),
            "source": source,
            "agentRole": agent_role,
            "type": event_type,
            "level": level,
            "message": self._redact(message),
            "data": self._redact(data or {}),
        }
        with (Path(run_dir) / "events.jsonl").open("a", encoding="utf-8") as f:
            f.write(json.dumps(event) + "\n")
        if event_sink:
            event_sink(event)

    def _cancelled(self, control):
        return bool(control and control.is_cancelled())

    def _wait_if_paused(self, control):
        if control:
            control.wait_if_paused()

    def run(self, task, repo_path, workflow, event_sink=None, control=None):
        adapter = RepoAdapter(
            repo_path,
            include_globs=self.config["repo"]["includeGlobs"],
            exclude_globs=self.config["repo"]["excludeGlobs"],
            allowlist=self.config.get("commands", {}).get("allowlist", []),
        )
        run_dir = self._run_dir(repo_path)
        self._emit_event(run_dir, "orchestrator", "run.started", "info", "Run started", {"workflow": workflow}, event_sink=event_sink)
        prompt_hash = hashlib.sha256(task.encode("utf-8")).hexdigest()
        run_log = {
            "workflow": workflow,
            "promptHash": prompt_hash,
            "agents": [
                {"role": "frontend", "model": self.frontend.model},
                {"role": "builder", "model": self.builder.model},
                {"role": "architecture", "model": self.architecture.model},
            ],
            "toolCalls": [],
            "status": "running",
        }

        for role, model in [("frontend", self.frontend.model), ("builder", self.builder.model), ("architecture", self.architecture.model)]:
            self._emit_event(
                run_dir,
                "agent",
                "agent.status",
                "info",
                f"{role} idle",
                {"status": "Idle", "model": model, "currentStep": "init"},
                agent_role=role,
                event_sink=event_sink,
            )

        self._wait_if_paused(control)
        if self._cancelled(control):
            run_log["status"] = "cancelled"
            self._write_json(run_dir / "run-log.json", run_log)
            self._write_final_summary(run_dir, adapter.changed_files(), [], {"findings": []}, [])
            self._emit_event(run_dir, "orchestrator", "run.cancelled", "warning", "Run cancelled", event_sink=event_sink)
            return run_dir
        context_files = adapter.list_files()
        run_log["toolCalls"].append({"type": "context_gather", "filesCount": len(context_files)})
        self._emit_event(run_dir, "repo", "tool.call", "info", "Context gathered", {"filesCount": len(context_files)}, event_sink=event_sink)

        if workflow == "arch_review_only":
            self._emit_event(run_dir, "agent", "agent.status", "info", "architecture running", {"status": "Running", "currentStep": "architecture_review"}, agent_role="architecture", event_sink=event_sink)
            review = self.architecture.review(adapter.diff(), adapter.changed_files())
            self._write_json(run_dir / "architecture-review.json", review)
            self._write_json(run_dir / "run-log.json", run_log)
            self._write_final_summary(run_dir, adapter.changed_files(), [], review, [])
            run_log["status"] = "completed"
            self._write_json(run_dir / "run-log.json", run_log)
            self._emit_event(run_dir, "agent", "agent.status", "info", "architecture completed", {"status": "Completed", "currentStep": "architecture_review"}, agent_role="architecture", event_sink=event_sink)
            self._emit_event(run_dir, "orchestrator", "run.completed", "info", "Run completed", event_sink=event_sink)
            return run_dir

        self._emit_event(run_dir, "agent", "agent.status", "info", "frontend running", {"status": "Running", "currentStep": "plan"}, agent_role="frontend", event_sink=event_sink)
        plan = self.frontend.plan(task, context_files)
        (run_dir / "plan.md").write_text(
            "# Frontend Plan\n\n"
            f"- Task: {task}\n"
            f"- Components: {', '.join(plan['components'])}\n"
            f"- Files to touch: {', '.join(plan['filesToTouch']) if plan['filesToTouch'] else 'none'}\n"
        )
        self._emit_event(run_dir, "agent", "agent.status", "info", "frontend completed", {"status": "Completed", "currentStep": "plan"}, agent_role="frontend", event_sink=event_sink)

        checks = []
        for key in ["format", "lint", "test"]:
            self._wait_if_paused(control)
            if self._cancelled(control):
                run_log["status"] = "cancelled"
                self._write_json(run_dir / "run-log.json", run_log)
                self._write_final_summary(run_dir, adapter.changed_files(), checks, {"findings": []}, [])
                self._emit_event(run_dir, "orchestrator", "run.cancelled", "warning", "Run cancelled", event_sink=event_sink)
                return run_dir
            result = adapter.run_command(self.config.get("commands", {}).get(key))
            checks.append(result)
            run_log["toolCalls"].append({"type": "command", "command": result["command"], "skipped": result["skipped"]})
            self._emit_event(
                run_dir,
                "command",
                "tool.call",
                "info" if result.get("returncode", 0) == 0 or result.get("skipped") else "error",
                f"Command {key} {'skipped' if result['skipped'] else 'executed'}",
                {"command": result["command"], "skipped": result["skipped"], "stdout": result.get("stdout", ""), "stderr": result.get("stderr", "")},
                event_sink=event_sink,
            )

        self._emit_event(run_dir, "agent", "agent.status", "info", "builder running", {"status": "Running", "currentStep": "implement"}, agent_role="builder", event_sink=event_sink)
        build_result = self.builder.implement(plan)
        self._emit_event(run_dir, "agent", "agent.step", "info", "builder tool activity", {"filesTouchedCount": len(build_result["filesTouched"])}, agent_role="builder", event_sink=event_sink)
        review = self.architecture.review(adapter.diff(), build_result["filesTouched"], build_result["appliedActions"])
        iterations = 0
        max_iterations = self.config["orchestration"]["maxIterations"]
        while any(f["severity"] == "high" for f in review["findings"]) and iterations < max_iterations:
            self._wait_if_paused(control)
            if self._cancelled(control):
                break
            iterations += 1
            build_result = self.builder.implement(plan, review["requiredActions"])
            review = self.architecture.review(adapter.diff(), build_result["filesTouched"], build_result["appliedActions"])
        self._emit_event(run_dir, "agent", "agent.status", "info", "builder completed", {"status": "Completed", "currentStep": "implement"}, agent_role="builder", event_sink=event_sink)
        self._emit_event(run_dir, "agent", "agent.status", "info", "architecture running", {"status": "Running", "currentStep": "architecture_review"}, agent_role="architecture", event_sink=event_sink)

        self._write_json(run_dir / "architecture-review.json", review)
        diff_text = adapter.diff()
        (run_dir / "changes.patch").write_text(diff_text)
        self._write_json(run_dir / "run-log.json", run_log)
        self._write_final_summary(run_dir, adapter.changed_files(), checks, review, [])
        run_log["status"] = "cancelled" if self._cancelled(control) else "completed"
        self._write_json(run_dir / "run-log.json", run_log)
        self._emit_event(
            run_dir,
            "agent",
            "agent.status",
            "info",
            "architecture completed",
            {"status": "Cancelled" if self._cancelled(control) else "Completed", "currentStep": "architecture_review"},
            agent_role="architecture",
            event_sink=event_sink,
        )
        self._emit_event(
            run_dir,
            "orchestrator",
            "run.completed" if run_log["status"] == "completed" else "run.cancelled",
            "info" if run_log["status"] == "completed" else "warning",
            "Run completed" if run_log["status"] == "completed" else "Run cancelled",
            event_sink=event_sink,
        )
        return run_dir

    def _write_final_summary(self, run_dir, changed_files, checks, review, unresolved):
        changed_lines = [f"- {f}" for f in changed_files] or ["- none"]
        check_lines = []
        for c in checks:
            status = "skipped" if c.get("skipped") else ("passed" if c.get("returncode") == 0 else "failed")
            check_lines.append(f"- {c.get('command')}: {status}")
        check_lines = check_lines or ["- none"]
        summary = [
            "# Final Summary",
            "",
            "## Changed files",
            *changed_lines,
            "",
            "## Tests/commands run",
            *check_lines,
            "",
            "## Architecture findings",
            f"- resolved high severity: {'yes' if not any(f['severity'] == 'high' for f in review['findings']) else 'no'}",
            f"- unresolved count: {len(unresolved)}",
        ]
        (run_dir / "final-summary.md").write_text("\n".join(summary))
