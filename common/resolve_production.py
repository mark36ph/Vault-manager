"""Orchestrate timeline preparation and portable Resolve package creation."""

from __future__ import annotations

import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable, Mapping

from common.resolve_portable_package import (
    PortableResolvePackageResult,
    export_portable_resolve_package,
)
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


class ResolveProductionService:
    """Prepare, persist, package, and optionally launch a Resolve production."""

    def __init__(
        self,
        *,
        process_runner: Callable[..., Any] = subprocess.Popen,
        python_executable: str | None = None,
        progress_callback: ProgressCallback | None = None,
    ) -> None:
        self.process_runner = process_runner
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
            project,
            folder,
            dict(settings),
            current,
            strict=strict,
            overwrite=overwrite,
        )

        launched = False
        command: tuple[str, ...] | None = None
        if launch:
            runner = package.package_folder / "build_resolve_timeline.py"
            if not runner.is_file():
                raise ResolveProductionError(f"Resolve runner was not created: {runner}")
            command = (self.python_executable, str(runner))
            self._progress("launch", 0.9, "Launching Resolve timeline builder")
            try:
                self.process_runner(command, cwd=package.package_folder)
            except OSError as error:
                raise ResolveProductionError(f"Could not launch Resolve builder: {error}") from error
            launched = True

        self._progress("complete", 1.0, "Resolve production is ready")
        return ResolveProductionResult(
            project_folder=folder,
            timeline_path=timeline_path,
            package=package,
            launched=launched,
            command=command,
            warnings=tuple(package.warnings),
        )


def build_resolve_production(
    project: Mapping[str, Any],
    project_folder: str | Path,
    settings: Mapping[str, Any],
    **options: Any,
) -> ResolveProductionResult:
    """Build a portable Resolve production using the default service."""
    return ResolveProductionService().run(project, project_folder, settings, **options)


def make_resolve_workflow_service(
    project_folder: str | Path,
    settings: Mapping[str, Any],
    *,
    service: ResolveProductionService | None = None,
    **options: Any,
):
    """Return a ProjectWorkflow-compatible timeline service."""
    producer = service or ResolveProductionService()

    def run(context):
        project = context.get("project")
        if not isinstance(project, Mapping):
            raise ResolveProductionError("workflow context does not contain a project mapping")
        return producer.run(project, project_folder, settings, **options)

    return run


__all__ = [
    "ResolveProductionError",
    "ResolveProductionResult",
    "ResolveProductionService",
    "build_resolve_production",
    "make_resolve_workflow_service",
]
