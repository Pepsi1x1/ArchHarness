import json
import tempfile
import unittest
from argparse import Namespace
from pathlib import Path

from src.config.loader import apply_cli_overrides, load_config
from src.harness.cli import _parse_init_git, build_parser
from src.harness.orchestrator import Orchestrator
from src.harness.tui import ConversationController, RunRequest


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
        self.assertFalse(_parse_init_git(args.init_git))
        self.assertTrue(_parse_init_git("true"))
        self.assertIsNone(_parse_init_git(None))

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

    def test_new_project_mode_can_skip_git_initialization(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            orchestrator = Orchestrator(load_config())
            orchestrator.run(
                "Bootstrap",
                str(root),
                "frontend_feature",
                workspace_mode="new-project",
                project_name="demo-app",
                init_git=False,
            )
            self.assertFalse((root / "demo-app" / ".git").exists())

    def test_existing_git_mode_requires_git_directory(self):
        with tempfile.TemporaryDirectory() as td:
            orchestrator = Orchestrator(load_config())
            with self.assertRaisesRegex(ValueError, "existing-git mode requires"):
                orchestrator.run("x", td, "frontend_feature", workspace_mode="existing-git")


class RunRequestTests(unittest.TestCase):
    def test_validate_raises_for_missing_task(self):
        req = RunRequest(
            workspaceMode="existing-folder",
            workspacePath="/tmp/foo",
            workflow="frontend_feature",
            taskPrompt="",
        )
        with self.assertRaisesRegex(ValueError, "taskPrompt is required"):
            req.validate()

    def test_validate_raises_for_missing_path(self):
        req = RunRequest(
            workspaceMode="existing-folder",
            workspacePath="",
            workflow="frontend_feature",
            taskPrompt="Do something",
        )
        with self.assertRaisesRegex(ValueError, "workspacePath is required"):
            req.validate()

    def test_validate_raises_for_new_project_without_name(self):
        req = RunRequest(
            workspaceMode="new-project",
            workspacePath="/tmp/root",
            workflow="frontend_feature",
            taskPrompt="Bootstrap app",
        )
        with self.assertRaisesRegex(ValueError, "projectName is required"):
            req.validate()

    def test_validate_passes_for_valid_request(self):
        req = RunRequest(
            workspaceMode="existing-folder",
            workspacePath="/tmp/foo",
            workflow="frontend_feature",
            taskPrompt="Add a login page",
        )
        req.validate()  # must not raise

    def test_validate_passes_for_new_project_with_name(self):
        req = RunRequest(
            workspaceMode="new-project",
            workspacePath="/tmp/root",
            workflow="frontend_feature",
            taskPrompt="Bootstrap app",
            projectName="MyApp",
        )
        req.validate()  # must not raise


class ConversationControllerTests(unittest.TestCase):
    def _make_controller(self):
        return ConversationController(load_config())

    def test_tui_assistant_config_defaults(self):
        config = load_config()
        tui_cfg = config.get("tui", {}).get("assistant", {})
        self.assertTrue(tui_cfg.get("enabled"))
        self.assertEqual(tui_cfg.get("model"), "gpt-5-mini")
        self.assertEqual(tui_cfg.get("temperature"), 0.2)
        self.assertTrue(tui_cfg.get("redaction", {}).get("enabled"))

    def test_extracts_new_project_intent_and_name(self):
        ctrl = self._make_controller()
        response, complete = ctrl.process_message(
            "Create a new React app called ClaimsPortal and add a login page."
        )
        self.assertEqual(ctrl._slots.get("workspaceMode"), "new-project")
        self.assertEqual(ctrl._slots.get("projectName"), "ClaimsPortal")
        self.assertIn("taskPrompt", ctrl._slots)

    def test_asks_for_missing_workspace_path(self):
        ctrl = self._make_controller()
        response, complete = ctrl.process_message("Add a login page")
        self.assertFalse(complete)
        self.assertIn("path", response.lower())

    def test_extracts_path_and_sets_mode(self):
        with tempfile.TemporaryDirectory() as td:
            ctrl = self._make_controller()
            response, complete = ctrl.process_message(
                f"Implement the feature in {td}"
            )
            self.assertEqual(ctrl._slots.get("workspacePath"), str(Path(td).resolve()))
            self.assertIn(ctrl._slots.get("workspaceMode"), ("existing-folder", "existing-git"))

    def test_arch_review_sets_workflow(self):
        ctrl = self._make_controller()
        ctrl.process_message("Architecture review of ./my-repo")
        self.assertEqual(ctrl._slots.get("workflow"), "arch_review_only")

    def test_model_override_via_use_sentence(self):
        ctrl = self._make_controller()
        ctrl.process_message("Use opus-4.6 for architecture review")
        overrides = ctrl._slots.get("modelOverrides") or {}
        self.assertIn("architectureModel", overrides)

    def test_inline_set_command_updates_path(self):
        ctrl = self._make_controller()
        with tempfile.TemporaryDirectory() as td:
            ctrl.update_slot("taskPrompt", "Add feature")
            ctrl.update_slot("workspacePath", "/tmp/old")
            ctrl.process_message(f"set workspace to {td}")
            self.assertEqual(ctrl._slots.get("workspacePath"), str(Path(td).resolve()))

    def test_build_run_request_returns_valid_object(self):
        with tempfile.TemporaryDirectory() as td:
            ctrl = self._make_controller()
            ctrl.update_slot("taskPrompt", "Add a login page")
            ctrl.update_slot("workspacePath", td)
            ctrl.update_slot("workspaceMode", "existing-folder")
            req = ctrl.build_run_request()
            self.assertIsInstance(req, RunRequest)
            self.assertEqual(req.taskPrompt, "Add a login page")
            self.assertEqual(req.workspacePath, td)
            self.assertIsNotNone(req.safety)
            self.assertEqual(req.safety["writeScopeRoot"], td)

    def test_summary_contains_key_fields(self):
        with tempfile.TemporaryDirectory() as td:
            ctrl = self._make_controller()
            ctrl.update_slot("taskPrompt", "Build feature")
            ctrl.update_slot("workspacePath", td)
            ctrl.update_slot("workspaceMode", "existing-folder")
            summary = ctrl.summary()
            self.assertIn("Build feature", summary)
            self.assertIn("existing-folder", summary)
            self.assertIn("gpt-5-mini", summary)

    def test_complete_flow_in_two_messages(self):
        """Simulate typical ≤3 interaction run: provide task+path upfront."""
        with tempfile.TemporaryDirectory() as td:
            ctrl = self._make_controller()
            # Message 1: everything in one shot
            response, complete = ctrl.process_message(
                f"Add a login page to the app in {td}"
            )
            self.assertTrue(complete, f"Expected complete, got response: {response}")
            req = ctrl.build_run_request()
            req.validate()

    def test_run_tui_accepts_no_path(self):
        """run_tui signature allows repo_path=None (archharness tui with no args)."""
        from src.harness.tui import run_tui
        import inspect
        sig = inspect.signature(run_tui)
        param = sig.parameters["repo_path"]
        self.assertIsNone(param.default)

    def test_cli_tui_command_path_is_optional(self):
        """archharness tui with no --path should parse successfully."""
        parser = build_parser()
        args = parser.parse_args(["tui"])
        self.assertEqual(args.command, "tui")
        self.assertIsNone(args.path)
        self.assertIsNone(args.repo)


if __name__ == "__main__":
    unittest.main()
