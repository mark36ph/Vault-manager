"""Persistence helpers that make a timeline part of a project folder."""

from __future__ import annotations

import json
import os
from pathlib import Path
from tempfile import NamedTemporaryFile

from .models import Timeline

TIMELINE_FILENAME = "timeline.json"


class TimelineStorageError(RuntimeError):
    """Raised when a project timeline cannot be read or written safely."""


class ProjectTimelineStore:
    """Load, create, and save the timeline owned by one project folder."""

    def __init__(self, project_folder: str | Path, *, filename: str = TIMELINE_FILENAME) -> None:
        self.project_folder = Path(project_folder)
        self.path = self.project_folder / filename

    def exists(self) -> bool:
        return self.path.is_file()

    def create(self, name: str, *, overwrite: bool = False, **timeline_options) -> Timeline:
        """Create and persist an empty project timeline.

        Existing timelines are preserved unless ``overwrite`` is explicitly true.
        """
        if self.exists() and not overwrite:
            return self.load()
        timeline = Timeline(name=name, **timeline_options)
        self.save(timeline)
        return timeline

    def ensure(self, name: str, **timeline_options) -> Timeline:
        """Load an existing timeline or create one for a legacy project."""
        if self.exists():
            return self.load()
        return self.create(name, **timeline_options)

    def load(self) -> Timeline:
        if not self.exists():
            raise FileNotFoundError(self.path)
        try:
            payload = json.loads(self.path.read_text(encoding="utf-8"))
        except (OSError, UnicodeError, json.JSONDecodeError) as error:
            raise TimelineStorageError(f"could not read timeline: {self.path}") from error
        if not isinstance(payload, dict):
            raise TimelineStorageError("timeline.json must contain a JSON object")
        try:
            return Timeline.from_dict(payload)
        except (KeyError, TypeError, ValueError) as error:
            raise TimelineStorageError(f"invalid timeline data: {self.path}") from error

    def save(self, timeline: Timeline) -> Path:
        """Atomically save a timeline as formatted UTF-8 JSON."""
        if not isinstance(timeline, Timeline):
            raise TypeError("timeline must be a Timeline")
        self.project_folder.mkdir(parents=True, exist_ok=True)
        serialized = json.dumps(timeline.to_dict(), indent=2, ensure_ascii=False) + "\n"
        temporary_path: Path | None = None
        try:
            with NamedTemporaryFile(
                mode="w",
                encoding="utf-8",
                newline="\n",
                dir=self.project_folder,
                prefix=f".{self.path.name}.",
                suffix=".tmp",
                delete=False,
            ) as temporary:
                temporary.write(serialized)
                temporary.flush()
                os.fsync(temporary.fileno())
                temporary_path = Path(temporary.name)
            temporary_path.replace(self.path)
        except OSError as error:
            if temporary_path is not None:
                temporary_path.unlink(missing_ok=True)
            raise TimelineStorageError(f"could not save timeline: {self.path}") from error
        return self.path


def ensure_project_timeline(
    project_folder: str | Path,
    name: str,
    **timeline_options,
) -> Timeline:
    """Convenience entry point for new and existing project workflows."""
    return ProjectTimelineStore(project_folder).ensure(name, **timeline_options)


__all__ = [
    "ProjectTimelineStore",
    "TIMELINE_FILENAME",
    "TimelineStorageError",
    "ensure_project_timeline",
]
