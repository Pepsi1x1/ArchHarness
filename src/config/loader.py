import copy
import json
from pathlib import Path


DEFAULT_CONFIG = {
    "agents": {
        "frontend": {"model": "sonnet-4.6"},
        "architecture": {"model": "opus-4.6"},
        "builder": {"model": "codex-5.3"},
    },
    "orchestration": {"maxIterations": 2},
    "repo": {"includeGlobs": ["**/*"], "excludeGlobs": [".agent-harness/**"]},
    "commands": {"format": None, "lint": None, "test": None, "allowlist": []},
    "output": {"mode": "patch"},
    "logging": {"level": "info"},
}


def _merge_dict(base, override):
    for key, value in (override or {}).items():
        if isinstance(value, dict) and isinstance(base.get(key), dict):
            _merge_dict(base[key], value)
        else:
            base[key] = value
    return base


def _load_yaml(path):
    try:
        import yaml
    except ImportError as exc:
        raise RuntimeError("YAML config requires PyYAML to be installed.") from exc
    return yaml.safe_load(path.read_text()) or {}


def load_config(config_path=None):
    config = copy.deepcopy(DEFAULT_CONFIG)
    if not config_path:
        return config

    path = Path(config_path)
    if path.suffix.lower() == ".json":
        override = json.loads(path.read_text())
    elif path.suffix.lower() in {".yml", ".yaml"}:
        override = _load_yaml(path)
    else:
        raise ValueError("Unsupported config format. Use JSON or YAML.")
    return _merge_dict(config, override)


def apply_cli_overrides(config, args):
    overrides = {"agents": {"frontend": {}, "architecture": {}, "builder": {}}}
    for role, arg_name in [
        ("frontend", "frontend_model"),
        ("architecture", "architecture_model"),
        ("builder", "builder_model"),
    ]:
        value = getattr(args, arg_name, None)
        if value:
            overrides["agents"][role]["model"] = value
    max_iterations = getattr(args, "max_iterations", None)
    output_mode = getattr(args, "output_mode", None)
    if max_iterations is not None:
        overrides["orchestration"] = {"maxIterations": max_iterations}
    if output_mode:
        overrides["output"] = {"mode": output_mode}
    return _merge_dict(config, overrides)
