"""Guarded deletion for orphan project folders discovered by integrity checks."""

from __future__ import annotations

import shutil
from pathlib import Path
from typing import Any


def delete_orphan_project(project_manager: Any, folder: str | Path) -> Path:
    """Delete one confirmed orphan project folder.

    The folder must exist as a direct child of a status folder inside the configured
    Projects root and must not be linked to any database project. This function never
    deletes database records.
    """
    projects_root = project_manager.get_projects_root().resolve()
    candidate = Path(folder).resolve()

    if not candidate.is_dir():
        raise FileNotFoundError(f"Orphan project folder does not exist:\n{candidate}")

    try:
        relative = candidate.relative_to(projects_root)
    except ValueError as error:
        raise ValueError(
            "Orphan project folder must be inside the configured Projects folder."
        ) from error

    if len(relative.parts) != 2:
        raise ValueError(
            "Orphan deletion requires a project folder directly inside a status folder."
        )

    for project in project_manager.db.get_projects():
        existing_folder = project_manager.resolve_project_folder(project).resolve()
        if existing_folder == candidate:
            raise ValueError(
                "This folder is linked to a database project and cannot be deleted as an orphan."
            )

    shutil.rmtree(candidate)
    return candidate


__all__ = ["delete_orphan_project"]
