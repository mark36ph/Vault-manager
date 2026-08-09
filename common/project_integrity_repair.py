"""Guarded repairs for project-integrity issues that are safe to fix automatically."""

from __future__ import annotations

from pathlib import Path
from typing import Any


SAFE_REPAIR_TYPES = {
    "stale_schedule",
    "absolute_path",
    "missing_folder_value",
}


def _project_value(project: Any, key: str, default: Any = "") -> Any:
    try:
        value = project[key]
    except (KeyError, IndexError, TypeError):
        return default
    return default if value is None else value


def _record(bucket: list[dict[str, Any]], issue: dict[str, Any], **extra: Any) -> None:
    item = dict(issue)
    item.update(extra)
    bucket.append(item)


def repair_safe_project_integrity(project_manager: Any) -> dict[str, list[dict[str, Any]]]:
    """Repair only integrity problems that can be resolved without moving/deleting files.

    Safe automatic repairs are deliberately narrow:
    - clear stale schedules from projects that are not Scheduled;
    - convert an absolute folder value to a relative value only when that folder exists,
      is inside the configured Projects root, and already sits under the database status;
    - restore a missing folder value only when the canonical status/title folder already exists.

    Ambiguous problems such as missing folders, orphan folders, invalid Scheduled dates,
    outside-root folders, and status/folder mismatches are reported but never changed.
    """
    issues = list(project_manager.check_project_integrity())
    projects = {
        int(_project_value(project, "id", -1)): project
        for project in project_manager.db.get_projects()
    }
    projects_root = project_manager.get_projects_root().resolve()

    repaired: list[dict[str, Any]] = []
    skipped: list[dict[str, Any]] = []
    errors: list[dict[str, Any]] = []

    for issue in issues:
        issue_type = str(issue.get("type") or "")
        project_id = issue.get("project_id")

        if issue_type not in SAFE_REPAIR_TYPES or project_id is None:
            _record(skipped, issue, reason="Requires manual review.")
            continue

        project = projects.get(int(project_id))
        if project is None:
            _record(skipped, issue, reason="Project no longer exists.")
            continue

        try:
            if issue_type == "stale_schedule":
                project_manager.db.update_project_schedule(int(project_id), "")
                _record(repaired, issue, action="Cleared stale schedule.")
                continue

            status = str(_project_value(project, "status", "") or "")
            scheduled_for = str(_project_value(project, "scheduled_for", "") or "")

            if issue_type == "missing_folder_value":
                candidate = project_manager.get_project_folder(project).resolve()
                if not candidate.is_dir():
                    _record(
                        skipped,
                        issue,
                        reason="Canonical project folder does not exist.",
                    )
                    continue
            else:
                candidate = Path(str(issue.get("folder") or "")).resolve()
                if not candidate.is_dir():
                    _record(skipped, issue, reason="Stored project folder does not exist.")
                    continue

            try:
                relative = candidate.relative_to(projects_root)
            except ValueError:
                _record(skipped, issue, reason="Project folder is outside the Projects root.")
                continue

            if not relative.parts or relative.parts[0] != status:
                _record(
                    skipped,
                    issue,
                    reason="Folder status does not match the database status.",
                )
                continue

            project_manager.db.update_project_status_and_folder(
                project_id=int(project_id),
                status=status,
                folder=str(relative),
                scheduled_for=scheduled_for,
            )
            _record(repaired, issue, action=f"Stored relative folder as {relative}.")
        except Exception as error:
            _record(errors, issue, error=str(error))

    return {
        "repaired": repaired,
        "skipped": skipped,
        "errors": errors,
    }


__all__ = ["SAFE_REPAIR_TYPES", "repair_safe_project_integrity"]
