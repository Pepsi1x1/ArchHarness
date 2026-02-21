import hashlib
import json
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

    def run(self, task, repo_path, workflow):
        adapter = RepoAdapter(
            repo_path,
            include_globs=self.config["repo"]["includeGlobs"],
            exclude_globs=self.config["repo"]["excludeGlobs"],
            allowlist=self.config.get("commands", {}).get("allowlist", []),
        )
        run_dir = self._run_dir(repo_path)
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
        }

        context_files = adapter.list_files()
        run_log["toolCalls"].append({"type": "context_gather", "filesCount": len(context_files)})

        if workflow == "arch_review_only":
            review = self.architecture.review(adapter.diff(), adapter.changed_files())
            self._write_json(run_dir / "architecture-review.json", review)
            self._write_json(run_dir / "run-log.json", run_log)
            self._write_final_summary(run_dir, adapter.changed_files(), [], review, [])
            return run_dir

        plan = self.frontend.plan(task, context_files)
        (run_dir / "plan.md").write_text(
            "# Frontend Plan\n\n"
            f"- Task: {task}\n"
            f"- Components: {', '.join(plan['components'])}\n"
            f"- Files to touch: {', '.join(plan['filesToTouch']) if plan['filesToTouch'] else 'none'}\n"
        )

        checks = []
        for key in ["format", "lint", "test"]:
            result = adapter.run_command(self.config.get("commands", {}).get(key))
            checks.append(result)
            run_log["toolCalls"].append({"type": "command", "command": result["command"], "skipped": result["skipped"]})

        build_result = self.builder.implement(plan)
        review = self.architecture.review(adapter.diff(), build_result["filesTouched"], build_result["appliedActions"])
        iterations = 0
        max_iterations = self.config["orchestration"]["maxIterations"]
        while any(f["severity"] == "high" for f in review["findings"]) and iterations < max_iterations:
            iterations += 1
            build_result = self.builder.implement(plan, review["requiredActions"])
            review = self.architecture.review(adapter.diff(), build_result["filesTouched"], build_result["appliedActions"])

        self._write_json(run_dir / "architecture-review.json", review)
        diff_text = adapter.diff()
        (run_dir / "changes.patch").write_text(diff_text)
        self._write_json(run_dir / "run-log.json", run_log)
        self._write_final_summary(run_dir, adapter.changed_files(), checks, review, [])
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
