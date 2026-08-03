"""Small reusable project-list filters."""
from __future__ import annotations

from typing import Any, Iterable, Mapping


def in_progress_projects(projects: Iterable[Mapping[str, Any]]) -> list[dict[str, Any]]:
    """Return dictionary copies of projects whose status is In Progress."""
    return [
        dict(project)
        for project in projects
        if str(project.get("status") or "").strip().casefold() == "in progress"
    ]


__all__ = ["in_progress_projects"]
