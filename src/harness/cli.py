import argparse
import json
from pathlib import Path

from src.config.loader import apply_cli_overrides, load_config
from src.harness.orchestrator import Orchestrator
from src.harness.tui import run_tui


def _parse_init_git(value):
    return None if value is None else value == "true"


def build_parser():
    parser = argparse.ArgumentParser(prog="archharness")
    sub = parser.add_subparsers(dest="command", required=True)
    run = sub.add_parser("run")
    run.add_argument("--task", required=True)
    run.add_argument("--mode", choices=["new-project", "existing-folder", "existing-git"], default="existing-git")
    run.add_argument("--path")
    run.add_argument("--repo")
    run.add_argument("--project-name")
    run.add_argument("--init-git", choices=["true", "false"])
    run.add_argument("--workflow", default="frontend_feature", choices=["frontend_feature", "arch_review_only"])
    run.add_argument("--config")
    run.add_argument("--frontend-model")
    run.add_argument("--architecture-model")
    run.add_argument("--builder-model")
    run.add_argument("--max-iterations", type=int)
    run.add_argument("--output-mode", choices=["patch", "branch"])
    tui = sub.add_parser("tui")
    tui.add_argument("--path")
    tui.add_argument("--repo")
    tui.add_argument("--config")
    return parser


def main(argv=None):
    parser = build_parser()
    args = parser.parse_args(argv)
    if args.command == "tui":
        run_tui(args.path or args.repo or ".", args.config)
        return
    workspace_path = args.path or args.repo
    if not workspace_path:
        parser.error("--path is required (or legacy --repo).")
    if args.mode == "new-project" and not args.project_name:
        parser.error("--project-name is required when --mode new-project.")
    if args.mode == "existing-folder" and not args.path:
        parser.error("--path is required when --mode existing-folder.")
    if args.mode == "existing-git" and not (Path(workspace_path) / ".git").exists():
        parser.error("existing-git mode requires --path/--repo to point to a folder containing .git.")
    init_git = _parse_init_git(args.init_git)
    config = apply_cli_overrides(load_config(args.config), args)
    orchestrator = Orchestrator(config)
    run_dir = orchestrator.run(
        args.task,
        workspace_path,
        args.workflow,
        workspace_mode=args.mode,
        project_name=args.project_name,
        init_git=init_git,
    )
    output = {"runDir": str(run_dir), "outputMode": config["output"]["mode"]}
    print(json.dumps(output))
    if config["output"]["mode"] == "branch":
        branch_note = Path(run_dir) / "branch-note.txt"
        branch_note.write_text("Branch mode selected. Create and push branch using your VCS workflow.")


if __name__ == "__main__":
    main()
