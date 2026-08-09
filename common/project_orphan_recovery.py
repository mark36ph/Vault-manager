"""Guarded recovery for orphan project folders discovered by integrity checks."""

from __future__ import annotations

from datetime import datetime
from pathlib import Path
from typing import Any


LEGACY_TEXT_FILES = {
    "Script.txt": "script",
    "Description.txt": "description",
    "Pinned Comment.txt": "pinned_comment",
    "Notes.txt": "notes",
}


def _project_value(project: Any, key: str, default: Any = "") -> Any:
    try:
        value = project[key]
    except (KeyError, IndexError, TypeError):
        return default
    return default if value is None else value


def _read_legacy_text(folder: Path) -> dict[str, str]:
    values = {field: "" for field in LEGACY_TEXT_FILES.values()}
    for filename, field in LEGACY_TEXT_FILES.items():
        path = folder / filename
        if not path.is_file():
            continue
        try:
            values[field] = path.read_text(encoding="utf-8").strip()
        except (OSError, UnicodeError):
            continue
    return values


def recover_orphan_project(
    project_manager: Any,
    folder: str | Path,
    category: str = "Misc",
    scheduled_for: str = "",
):
    """Create a database record for one existing orphan project folder.

    The folder must already be a direct child of a status folder inside the configured
    Projects root. Recovery never moves, renames, or deletes files. The status and title
    are inferred from the existing path so the recovered database record matches disk.
    Legacy text files are imported when present. Scheduled folders require a valid future
    schedule so recovery does not create a new integrity problem.
    """
    projects_root = project_manager.get_projects_root().resolve()
    candidate = Path(folder).resolve()

    if not candidate.is_dir():
        raise FileNotFoundError(f"Orphan project folder does not exist:\n{candidate}")

    try:
        relative = candidate.relative_to(projects_root)
    except ValueError as error:
        raise ValueError("Orphan project folder must be inside the configured Projects folder.") from error

    if len(relative.parts) != 2:
        raise ValueError(
            "Orphan recovery requires a project folder directly inside a status folder."
        )

    status, title = relative.parts
    status = str(status).strip()
    title = str(title).strip()
    category = str(category or "Misc").strip() or "Misc"

    if not status or not title:
        raise ValueError("Could not determine the project status and title from the folder path.")

    if status == "Scheduled":
        scheduled_for = project_manager._validated_schedule(scheduled_for)
    else:
        scheduled_for = ""

    for project in project_manager.db.get_projects():
        existing_folder = project_manager.resolve_project_folder(project).resolve()
        if existing_folder == candidate:
            raise ValueError("This folder is already linked to a database project.")

    legacy = _read_legacy_text(candidate)
    created = datetime.now().strftime("%Y-%m-%d %H:%M")

    project_manager.db.add_project(
        title=title,
        category=category,
        status=status,
        folder=str(relative),
        created=created,
        script=legacy["script"],
        description=legacy["description"],
        pinned_comment=legacy["pinned_comment"],
        notes=legacy["notes"],
    )

    recovered = project_manager.db.get_latest_project()
    if recovered is None:
        raise RuntimeError("The orphan folder was not recovered into the database.")

    project_id = recovered["id"]
    if status == "Scheduled":
        try:
            project_manager.db.update_project_schedule(project_id, scheduled_for)
        except Exception:
            project_manager.db.delete_project(project_id)
            raise
        recovered = project_manager.db.get_project(project_id)

    return recovered


__all__ = ["recover_orphan_project"]
