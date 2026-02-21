import json
import tempfile
import unittest
from argparse import Namespace
from pathlib import Path

from src.config.loader import apply_cli_overrides, load_config
from src.harness.cli import build_parser
from src.harness.orchestrator import Orchestrator


class HarnessTests(unittest.TestCase):
    def test_default_models_and_cli_overrides(self):
        with tempfile.TemporaryDirectory() as td:
            config_path = Path(td) / "config.json"
            config_path.write_text(json.dumps({"agents": {"frontend": {"model": "custom-frontend"}}}))
            loaded = load_config(str(config_path))
            self.assertEqual(loaded["agents"]["frontend"]["model"], "custom-frontend")
            self.assertEqual(loaded["agents"]["architecture"]["model"], "opus-4.6")
            self.assertEqual(loaded["agents"]["builder"]["model"], "codex-5.3")

            args = Namespace(
                frontend_model=None,
                architecture_model=None,
                builder_model="cli-builder",
                max_iterations=None,
                output_mode=None,
            )
            merged = apply_cli_overrides(loaded, args)
            self.assertEqual(merged["agents"]["builder"]["model"], "cli-builder")

    def test_orchestrator_writes_required_artifacts(self):
        with tempfile.TemporaryDirectory() as td:
            repo = Path(td)
            (repo / "file.txt").write_text("sample")
            config = load_config()
            orchestrator = Orchestrator(config)
            run_dir = orchestrator.run("Build feature", str(repo), "frontend_feature", workspace_mode="existing-folder")
            self.assertTrue((run_dir / "plan.md").exists())
            self.assertTrue((run_dir / "architecture-review.json").exists())
            self.assertTrue((run_dir / "final-summary.md").exists())
            review = json.loads((run_dir / "architecture-review.json").read_text())
            self.assertIn("findings", review)
            run_log = json.loads((run_dir / "run-log.json").read_text())
            self.assertEqual(run_log["status"], "completed")
            with (run_dir / "events.jsonl").open() as f:
                events = [json.loads(line) for line in f if line.strip()]
            self.assertTrue(events)
            self.assertTrue(all("runId" in event and "source" in event and "message" in event for event in events))

    def test_cli_parser_supports_tui(self):
        parser = build_parser()
        args = parser.parse_args(["tui", "--repo", "/tmp/demo"])
        self.assertEqual(args.command, "tui")
        self.assertEqual(args.repo, "/tmp/demo")

    def test_cli_parser_supports_workspace_mode_flags(self):
        parser = build_parser()
        args = parser.parse_args(
            [
                "run",
                "--task",
                "Create starter app",
                "--mode",
                "new-project",
                "--path",
                "/tmp/root",
                "--project-name",
                "demo",
                "--init-git",
                "false",
            ]
        )
        self.assertEqual(args.mode, "new-project")
        self.assertEqual(args.project_name, "demo")
        self.assertEqual(args.path, "/tmp/root")
        self.assertEqual(args.init_git, "false")

    def test_orchestrator_marks_cancelled_status(self):
        class CancelledControl:
            def is_cancelled(self):
                return True

            def wait_if_paused(self):
                return

        with tempfile.TemporaryDirectory() as td:
            repo = Path(td)
            (repo / "file.txt").write_text("sample")
            orchestrator = Orchestrator(load_config())
            run_dir = orchestrator.run("Stop early", str(repo), "frontend_feature", workspace_mode="existing-folder", control=CancelledControl())
            run_log = json.loads((run_dir / "run-log.json").read_text())
            self.assertEqual(run_log["status"], "cancelled")

    def test_new_project_mode_creates_workspace_and_git_by_default(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            orchestrator = Orchestrator(load_config())
            run_dir = orchestrator.run(
                "Bootstrap",
                str(root),
                "frontend_feature",
                workspace_mode="new-project",
                project_name="demo-app",
            )
            workspace = root / "demo-app"
            self.assertTrue(workspace.exists())
            self.assertTrue((workspace / ".git").exists())
            run_log = json.loads((run_dir / "run-log.json").read_text())
            self.assertEqual(run_log["workspaceMode"], "new-project")

    def test_existing_git_mode_requires_git_directory(self):
        with tempfile.TemporaryDirectory() as td:
            orchestrator = Orchestrator(load_config())
            with self.assertRaisesRegex(ValueError, "existing-git mode requires"):
                orchestrator.run("x", td, "frontend_feature", workspace_mode="existing-git")


if __name__ == "__main__":
    unittest.main()
