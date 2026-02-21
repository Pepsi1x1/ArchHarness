import fnmatch
import subprocess
from pathlib import Path


class RepoAdapter:
    def __init__(self, repo_path, include_globs=None, exclude_globs=None, allowlist=None):
        self.repo_path = Path(repo_path).resolve()
        self.include_globs = include_globs or ["**/*"]
        self.exclude_globs = exclude_globs or []
        self.allowlist = allowlist or []

    def _is_included(self, rel_path):
        included = any(fnmatch.fnmatch(rel_path, pattern) for pattern in self.include_globs)
        excluded = any(fnmatch.fnmatch(rel_path, pattern) for pattern in self.exclude_globs)
        return included and not excluded

    def list_files(self):
        files = []
        for path in self.repo_path.rglob("*"):
            if path.is_file():
                rel = str(path.relative_to(self.repo_path))
                if self._is_included(rel):
                    files.append(rel)
        return sorted(files)

    def run_command(self, command):
        if not command:
            return {"command": command, "skipped": True, "stdout": "", "stderr": ""}
        first = command.split()[0]
        if self.allowlist and first not in self.allowlist:
            return {"command": command, "skipped": True, "stdout": "", "stderr": "Command not allowlisted"}
        proc = subprocess.run(
            command,
            cwd=self.repo_path,
            shell=True,
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
        proc = subprocess.run(
            "git --no-pager diff -- .",
            cwd=self.repo_path,
            shell=True,
            check=False,
            capture_output=True,
            text=True,
        )
        return proc.stdout if proc.returncode == 0 else ""

    def changed_files(self):
        proc = subprocess.run(
            "git --no-pager diff --name-only -- .",
            cwd=self.repo_path,
            shell=True,
            check=False,
            capture_output=True,
            text=True,
        )
        if proc.returncode != 0:
            return []
        return [line.strip() for line in proc.stdout.splitlines() if line.strip()]
