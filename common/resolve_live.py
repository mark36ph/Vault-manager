"""Connect to DaVinci Resolve and build a real project from an export package."""
from __future__ import annotations

import importlib
import json
import subprocess
import sys
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable, Mapping

from common.resolve_timeline_builder import ResolveTimelineBuildResult, build_resolve_timeline


class LiveResolveError(RuntimeError):
    """Raised when Resolve cannot be launched, connected to, or automated."""


@dataclass(frozen=True)
class LiveResolveResult:
    package_folder: Path
    project_name: str
    timeline_name: str
    imported_media: int
    placed_clips: int
    markers: int
    launched_application: bool
    warnings: tuple[str, ...]


def _known_module_paths() -> tuple[Path, ...]:
    return (
        Path(r"C:\ProgramData\Blackmagic Design\DaVinci Resolve\Support\Developer\Scripting\Modules"),
        Path(r"C:\Program Files\Blackmagic Design\DaVinci Resolve\Developer\Scripting\Modules"),
    )


def _module(settings: Mapping[str, Any]):
    configured = str(settings.get("scripting_module_path") or "").strip()
    candidates = ([Path(configured)] if configured else []) + list(_known_module_paths())
    for folder in candidates:
        if folder.is_dir() and str(folder) not in sys.path:
            sys.path.insert(0, str(folder))
    try:
        return importlib.import_module("DaVinciResolveScript")
    except ImportError as error:
        raise LiveResolveError(
            "DaVinciResolveScript could not be imported. Set the Resolve scripting module path in Settings."
        ) from error


def _absolute_plan(plan: dict[str, Any], package_folder: Path) -> dict[str, Any]:
    for track in plan.get("tracks", []):
        for clip in track.get("clips", []):
            source = str(clip.get("source") or "").strip()
            if source and clip.get("kind") in {"image", "video", "audio"}:
                path = Path(source)
                if not path.is_absolute():
                    path = package_folder / path
                clip["source"] = str(path.resolve())
    return plan


class LiveResolveService:
    """Launch Resolve when required, connect, build the timeline, and save it."""

    def __init__(
        self,
        *,
        process_runner: Callable[..., Any] = subprocess.Popen,
        sleeper: Callable[[float], None] = time.sleep,
        attempts: int = 30,
        interval: float = 1.0,
    ) -> None:
        self.process_runner = process_runner
        self.sleeper = sleeper
        self.attempts = attempts
        self.interval = interval

    def _connect(self, module: Any):
        for index in range(max(1, self.attempts)):
            resolve = module.scriptapp("Resolve")
            if resolve is not None:
                return resolve
            if index + 1 < self.attempts:
                self.sleeper(self.interval)
        raise LiveResolveError("Could not connect to DaVinci Resolve. Ensure external scripting is enabled.")

    def build_package(
        self,
        package_folder: str | Path,
        settings: Mapping[str, Any],
        *,
        launch_if_needed: bool = True,
    ) -> LiveResolveResult:
        folder = Path(package_folder).resolve()
        plan_path = folder / "resolve_timeline_plan.json"
        if not plan_path.is_file():
            raise LiveResolveError(f"Resolve timeline plan was not found: {plan_path}")
        try:
            plan = json.loads(plan_path.read_text(encoding="utf-8"))
        except (OSError, UnicodeError, json.JSONDecodeError) as error:
            raise LiveResolveError(f"Could not read Resolve timeline plan: {plan_path}") from error

        module = _module(settings)
        launched = False
        resolve = module.scriptapp("Resolve")
        if resolve is None and launch_if_needed:
            executable = str(settings.get("application_path") or "").strip()
            if not executable:
                executable = r"C:\Program Files\Blackmagic Design\DaVinci Resolve\Resolve.exe"
            if not Path(executable).is_file():
                raise LiveResolveError("DaVinci Resolve executable was not found. Set its path in Settings.")
            self.process_runner([executable])
            launched = True
            resolve = self._connect(module)
        elif resolve is None:
            raise LiveResolveError("DaVinci Resolve is not running.")

        build: ResolveTimelineBuildResult = build_resolve_timeline(
            resolve, _absolute_plan(plan, folder), project_name=str(plan.get("name") or "Fact Vault Video")
        )
        manager = resolve.GetProjectManager()
        project = manager.GetCurrentProject() if manager is not None else None
        if project is not None and hasattr(project, "Save"):
            project.Save()
        elif manager is not None and hasattr(manager, "SaveProject"):
            manager.SaveProject()
        return LiveResolveResult(
            package_folder=folder,
            project_name=build.project_name,
            timeline_name=build.timeline_name,
            imported_media=build.imported_media,
            placed_clips=build.placed_clips,
            markers=build.markers,
            launched_application=launched,
            warnings=build.warnings,
        )


__all__ = ["LiveResolveError", "LiveResolveResult", "LiveResolveService"]
