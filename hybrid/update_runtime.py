"""Safe Git update helpers for the hybrid desktop app."""
from __future__ import annotations

import subprocess
from pathlib import Path
from typing import Any


class HybridUpdateError(RuntimeError):
    """Raised when the checkout cannot be safely updated."""


class HybridUpdateRuntime:
    def __init__(self, repository_root: str | Path) -> None:
        self.root = Path(repository_root).resolve()

    def _git(self, *args: str) -> str:
        completed = subprocess.run(
            ["git", *args],
            cwd=self.root,
            check=False,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
        )
        output = "\n".join(
            part.strip()
            for part in (completed.stdout, completed.stderr)
            if part and part.strip()
        ).strip()
        if completed.returncode != 0:
            raise HybridUpdateError(output or f"git {' '.join(args)} failed")
        return output

    def status(self) -> dict[str, Any]:
        branch = self._git("branch", "--show-current").strip() or "(detached)"
        local = self._git("rev-parse", "HEAD").strip()
        dirty = bool(self._git("status", "--porcelain").strip())

        remote = ""
        behind = 0
        ahead = 0
        try:
            self._git("fetch", "origin", branch)
            remote = self._git("rev-parse", f"origin/{branch}").strip()
            counts = self._git("rev-list", "--left-right", "--count", f"HEAD...origin/{branch}").split()
            if len(counts) == 2:
                ahead, behind = int(counts[0]), int(counts[1])
        except (HybridUpdateError, ValueError):
            remote = ""

        return {
            "branch": branch,
            "local_commit": local,
            "remote_commit": remote,
            "dirty": dirty,
            "ahead": ahead,
            "behind": behind,
            "update_available": bool(remote and behind > 0),
        }

    def update(self) -> dict[str, Any]:
        branch = self._git("branch", "--show-current").strip()
        if not branch:
            raise HybridUpdateError("Cannot update from a detached Git checkout.")

        dirty = self._git("status", "--porcelain").strip()
        if dirty:
            raise HybridUpdateError(
                "Local changes are present. Commit, discard, or stash them before updating."
            )

        before = self._git("rev-parse", "HEAD").strip()
        self._git("fetch", "origin", branch)
        pull_output = self._git("pull", "--ff-only", "origin", branch)
        after = self._git("rev-parse", "HEAD").strip()

        return {
            "branch": branch,
            "before": before,
            "after": after,
            "updated": before != after,
            "message": pull_output or ("Updated." if before != after else "Already up to date."),
        }


__all__ = ["HybridUpdateError", "HybridUpdateRuntime"]
