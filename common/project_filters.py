"""Small reusable project-list filters."""
from __future__ import annotations

from typing import Any, Iterable


def in_progress_projects(projects: Iterable[Any]) -> list[dict[str, Any]]:
    """Return dictionary copies of projects whose status is In Progress.

    Database queries return sqlite3.Row objects, which support dict(row) but do
    not provide Mapping.get(). Normalize each row first so this filter works for
    both database rows and ordinary dictionaries.
    """
    matches = []

    for project in projects:
        item = dict(project)
        if str(item.get("status") or "").strip().casefold() == "in progress":
            matches.append(item)

    return matches


__all__ = ["in_progress_projects"]
