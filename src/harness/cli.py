import argparse
import json
from pathlib import Path

from src.config.loader import apply_cli_overrides, load_config
from src.harness.orchestrator import Orchestrator
from src.harness.tui import run_tui


def build_parser():
    parser = argparse.ArgumentParser(prog="archharness")
    sub = parser.add_subparsers(dest="command", required=True)
    run = sub.add_parser("run")
    run.add_argument("--task", required=True)
    run.add_argument("--repo", required=True)
    run.add_argument("--workflow", default="frontend_feature", choices=["frontend_feature", "arch_review_only"])
    run.add_argument("--config")
    run.add_argument("--frontend-model")
    run.add_argument("--architecture-model")
    run.add_argument("--builder-model")
    run.add_argument("--max-iterations", type=int)
    run.add_argument("--output-mode", choices=["patch", "branch"])
    tui = sub.add_parser("tui")
    tui.add_argument("--repo", required=True)
    tui.add_argument("--config")
    return parser


def main(argv=None):
    parser = build_parser()
    args = parser.parse_args(argv)
    if args.command == "tui":
        run_tui(args.repo, args.config)
        return
    config = apply_cli_overrides(load_config(args.config), args)
    orchestrator = Orchestrator(config)
    run_dir = orchestrator.run(args.task, args.repo, args.workflow)
    output = {"runDir": str(run_dir), "outputMode": config["output"]["mode"]}
    print(json.dumps(output))
    if config["output"]["mode"] == "branch":
        branch_note = Path(run_dir) / "branch-note.txt"
        branch_note.write_text("Branch mode selected. Create and push branch using your VCS workflow.")


if __name__ == "__main__":
    main()
