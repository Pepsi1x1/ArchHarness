import difflib
import fnmatch
import hashlib
import shlex
import subprocess
from pathlib import Path


class WorkspaceAdapter:
    def __init__(self, workspace_path, include_globs=None, exclude_globs=None, allowlist=None):
        self.workspace_path = Path(workspace_path).resolve()
        self.include_globs = include_globs or ["**/*"]
        self.exclude_globs = exclude_globs or []
        self.allowlist = allowlist or []
        self._baseline_snapshot = {}

    def _is_included(self, rel_path):
        included = any(fnmatch.fnmatch(rel_path, pattern) for pattern in self.include_globs)
        excluded = any(fnmatch.fnmatch(rel_path, pattern) for pattern in self.exclude_globs)
        return included and not excluded

    def _snapshot(self):
        snapshot = {}
        if not self.workspace_path.exists():
            return snapshot
        for path in self.workspace_path.rglob("*"):
            if not path.is_file():
                continue
            rel = str(path.relative_to(self.workspace_path))
            if not self._is_included(rel):
                continue
            data = path.read_bytes()
            snapshot[rel] = {
                "hash": hashlib.sha256(data).hexdigest(),
                "text": data.decode("utf-8", errors="replace"),
            }
        return snapshot

    def capture_baseline(self):
        self._baseline_snapshot = self._snapshot()

    def list_files(self):
        return sorted(self._snapshot().keys())

    def run_command(self, command):
        if not command:
            return {"command": command, "skipped": True, "stdout": "", "stderr": ""}
        cmd = shlex.split(command)
        first = Path(cmd[0]).name if cmd else ""
        if self.allowlist and first not in self.allowlist:
            return {"command": command, "skipped": True, "stdout": "", "stderr": "Command not allowlisted"}
        proc = subprocess.run(
            cmd,
            cwd=self.workspace_path,
            check=False,
            capture_output=True,
            text=True,
        )
        return {
            "command": command,
            "skipped": False,
            "returncode": proc.returncode,
            "stdout": proc.stdout,
            "stderr": proc.stderr,
        }

    def diff(self):
        current = self._snapshot()
        changed = sorted(set(self._baseline_snapshot) | set(current))
        lines = []
        for rel in changed:
            before_entry = self._baseline_snapshot.get(rel)
            after_entry = current.get(rel)
            if before_entry == after_entry:
                continue
            before = (before_entry or {}).get("text", "").splitlines(keepends=True)
            after = (after_entry or {}).get("text", "").splitlines(keepends=True)
            lines.extend(
                difflib.unified_diff(
                    before,
                    after,
                    fromfile=f"a/{rel}",
                    tofile=f"b/{rel}",
                    lineterm="",
                )
            )
        return "\n".join(lines) + ("\n" if lines else "")

    def changed_files(self):
        current = self._snapshot()
        changed = sorted(set(self._baseline_snapshot) | set(current))
        return [rel for rel in changed if self._baseline_snapshot.get(rel, {}).get("hash") != current.get(rel, {}).get("hash")]

    def initialize_project(self, project_name=None):
        if project_name:
            target = (self.workspace_path / project_name).resolve()
            try:
                target.relative_to(self.workspace_path)
            except ValueError as exc:
                raise ValueError("Project path escapes target root.") from exc
            target.mkdir(parents=True, exist_ok=False)
            self.workspace_path = target
        else:
            self.workspace_path.mkdir(parents=True, exist_ok=True)
        self.capture_baseline()
        return self.workspace_path

    def initialize_git(self):
        proc = subprocess.run(
            ["git", "init"],
            cwd=self.workspace_path,
            check=False,
            capture_output=True,
            text=True,
        )
        return proc.returncode == 0

    def metadata(self):
        return {"isGit": False, "branch": None, "workspacePath": str(self.workspace_path)}


class FileSystemWorkspaceAdapter(WorkspaceAdapter):
    pass


class GitWorkspaceAdapter(WorkspaceAdapter):
    def metadata(self):
        branch = None
        proc = subprocess.run(
            ["git", "rev-parse", "--abbrev-ref", "HEAD"],
            cwd=self.workspace_path,
            check=False,
            capture_output=True,
            text=True,
        )
        if proc.returncode == 0:
            branch = proc.stdout.strip() or None
        return {"isGit": True, "branch": branch, "workspacePath": str(self.workspace_path)}


# Backwards compatibility alias
RepoAdapter = GitWorkspaceAdapter
