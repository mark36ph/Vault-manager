"""Guarded recovery for orphan project folders discovered by integrity checks."""

from __future__ import annotations

import shutil
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
    target_status: str | None = None,
):
    """Create a database record for one existing orphan project folder.

    Recovery never puts an orphan back into Scheduled. A folder found under Scheduled
    defaults to In Progress. Callers may instead choose another non-Scheduled status.
    When the chosen status differs from the folder's current status, the existing folder
    is moved intact and restored to its original location if the database insert fails.
    Legacy text files are imported when present.
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

    source_status, title = relative.parts
    source_status = str(source_status).strip()
    title = str(title).strip()
    category = str(category or "Misc").strip() or "Misc"
    target_status = str(target_status or "").strip()

    if not source_status or not title:
        raise ValueError("Could not determine the project status and title from the folder path.")

    if not target_status:
        target_status = "In Progress" if source_status == "Scheduled" else source_status

    if target_status == "Scheduled":
        raise ValueError("Recovered orphan projects cannot be returned to Scheduled.")

    for project in project_manager.db.get_projects():
        existing_folder = project_manager.resolve_project_folder(project).resolve()
        if existing_folder == candidate:
            raise ValueError("This folder is already linked to a database project.")

    destination = (projects_root / target_status / title).resolve()
    moved = False

    if destination != candidate:
        if destination.exists():
            raise FileExistsError(
                f"The recovery destination already exists:\n{destination}"
            )
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.move(str(candidate), str(destination))
        moved = True

    legacy = _read_legacy_text(destination)
    created = datetime.now().strftime("%Y-%m-%d %H:%M")
    destination_relative = destination.relative_to(projects_root)

    try:
        project_manager.db.add_project(
            title=title,
            category=category,
            status=target_status,
            folder=str(destination_relative),
            created=created,
            script=legacy["script"],
            description=legacy["description"],
            pinned_comment=legacy["pinned_comment"],
            notes=legacy["notes"],
        )
    except Exception:
        if moved and destination.exists() and not candidate.exists():
            candidate.parent.mkdir(parents=True, exist_ok=True)
            shutil.move(str(destination), str(candidate))
        raise

    recovered = project_manager.db.get_latest_project()
    if recovered is None:
        if moved and destination.exists() and not candidate.exists():
            candidate.parent.mkdir(parents=True, exist_ok=True)
            shutil.move(str(destination), str(candidate))
        raise RuntimeError("The orphan folder was not recovered into the database.")

    return recovered


__all__ = ["recover_orphan_project"]
