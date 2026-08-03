"""Orchestrate timeline preparation, portable packaging, and live Resolve creation."""
from __future__ import annotations

import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable, Mapping

from common.resolve_live import LiveResolveResult, LiveResolveService
from common.resolve_portable_package import PortableResolvePackageResult, export_portable_resolve_package
from timeline import ProjectTimelineStore, Timeline, materialize_timeline_clips

ProgressCallback = Callable[[str, float, str], None]


class ResolveProductionError(RuntimeError):
    """Raised when the one-click Resolve production workflow cannot complete."""


@dataclass(frozen=True)
class ResolveProductionResult:
    project_folder: Path
    timeline_path: Path
    package: PortableResolvePackageResult
    launched: bool
    command: tuple[str, ...] | None
    warnings: tuple[str, ...]
    live: LiveResolveResult | None = None


class ResolveProductionService:
    """Prepare, persist, package, and optionally build inside live Resolve."""

    def __init__(
        self,
        *,
        live_service: LiveResolveService | None = None,
        process_runner: Callable[..., Any] | None = None,
        python_executable: str | None = None,
        progress_callback: ProgressCallback | None = None,
    ) -> None:
        self.live_service = live_service or LiveResolveService()
        self.legacy_runner = process_runner
        self.python_executable = python_executable or sys.executable
        self.progress_callback = progress_callback

    def _progress(self, stage: str, fraction: float, message: str) -> None:
        if self.progress_callback is not None:
            self.progress_callback(stage, fraction, message)

    @staticmethod
    def _project_title(project: Mapping[str, Any]) -> str:
        return str(project.get("title") or "Fact Vault Video")

    def run(
        self,
        project: Mapping[str, Any],
        project_folder: str | Path,
        settings: Mapping[str, Any],
        *,
        timeline: Timeline | None = None,
        materialize: bool = True,
        strict: bool = True,
        overwrite: bool = True,
        launch: bool = False,
    ) -> ResolveProductionResult:
        if not isinstance(project, Mapping):
            raise TypeError("project must be a mapping")
        if not isinstance(settings, Mapping):
            raise TypeError("settings must be a mapping")
        folder = Path(project_folder)
        if not folder.is_dir():
            raise FileNotFoundError(f"Project folder could not be found: {folder}")

        store = ProjectTimelineStore(folder)
        self._progress("timeline", 0.1, "Loading project timeline")
        current = timeline or store.ensure(
            self._project_title(project),
            width=int(settings.get("timeline_width", 1080)),
            height=int(settings.get("timeline_height", 1920)),
            frame_rate=float(settings.get("frame_rate", 30)),
        )
        if not isinstance(current, Timeline):
            raise TypeError("timeline must be a Timeline")
        if materialize:
            self._progress("timeline", 0.3, "Materializing assigned assets")
            materialize_timeline_clips(current)
        timeline_path = store.save(current)

        self._progress("package", 0.55, "Building portable Resolve package")
        package = export_portable_resolve_package(
            project, folder, dict(settings), current, strict=strict, overwrite=overwrite
        )

        live = None
        command = None
        launched = False
        if launch and self.legacy_runner is not None:
            runner = package.package_folder / "build_resolve_timeline.py"
            command = (self.python_executable, str(runner))
            self._progress("launch", 0.78, "Launching Resolve timeline builder")
            try:
                self.legacy_runner(command, cwd=package.package_folder)
            except OSError as error:
                raise ResolveProductionError(f"Could not launch Resolve builder: {error}") from error
            launched = True
        elif launch:
            self._progress("launch", 0.78, "Connecting to DaVinci Resolve")
            try:
                live = self.live_service.build_package(package.package_folder, settings, launch_if_needed=True)
            except Exception as error:
                raise ResolveProductionError(f"Could not build the live Resolve project: {error}") from error
            launched = True

        warnings = list(package.warnings)
        if live is not None:
            warnings.extend(live.warnings)
        self._progress("complete", 1.0, "Resolve project is ready" if live else "Resolve package is ready")
        return ResolveProductionResult(
            project_folder=folder,
            timeline_path=timeline_path,
            package=package,
            launched=launched,
            command=command,
            warnings=tuple(warnings),
            live=live,
        )


def build_resolve_production(project, project_folder, settings, **options):
    return ResolveProductionService().run(project, project_folder, settings, **options)


def make_resolve_workflow_service(project_folder, settings, *, service=None, **options):
    producer = service or ResolveProductionService()
    def run(context):
        project = context.get("project")
        if not isinstance(project, Mapping):
            raise ResolveProductionError("workflow context does not contain a project mapping")
        return producer.run(project, project_folder, settings, **options)
    return run


__all__ = [
    "ResolveProductionError", "ResolveProductionResult", "ResolveProductionService",
    "build_resolve_production", "make_resolve_workflow_service",
]
