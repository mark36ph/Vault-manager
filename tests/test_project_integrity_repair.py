from pathlib import Path

from common.project_integrity_repair import repair_safe_project_integrity


def _project_by_title(pm, title):
    return next(row for row in pm.db.get_projects() if row["title"] == title)


def test_repair_clears_stale_schedule(project_manager):
    pm = project_manager
    pm.create_project(
        title="Repair Stale Schedule",
        category="Testing",
        status="Completed",
    )
    project = _project_by_title(pm, "Repair Stale Schedule")
    pm.db.update_project_schedule(project["id"], "2099-01-01 12:00")

    result = repair_safe_project_integrity(pm)
    repaired = pm.db.get_project(project["id"])

    assert repaired["scheduled_for"] == ""
    assert any(item["type"] == "stale_schedule" for item in result["repaired"])
    assert not any(item["type"] == "stale_schedule" for item in pm.check_project_integrity())


def test_repair_normalizes_safe_absolute_folder_path(project_manager):
    pm = project_manager
    pm.create_project(
        title="Repair Absolute Path",
        category="Testing",
        status="Completed",
    )
    project = _project_by_title(pm, "Repair Absolute Path")
    folder = pm.resolve_project_folder(project).resolve()

    pm.db.update_project_status_and_folder(
        project_id=project["id"],
        status="Completed",
        folder=str(folder),
        scheduled_for="",
    )

    result = repair_safe_project_integrity(pm)
    repaired = pm.db.get_project(project["id"])

    assert repaired["folder"] == str(Path("Completed") / "Repair Absolute Path")
    assert folder.is_dir()
    assert any(item["type"] == "absolute_path" for item in result["repaired"])


def test_repair_restores_missing_folder_value_when_canonical_folder_exists(project_manager):
    pm = project_manager
    pm.create_project(
        title="Repair Missing Folder Value",
        category="Testing",
        status="In Progress",
    )
    project = _project_by_title(pm, "Repair Missing Folder Value")
    folder = pm.resolve_project_folder(project)

    pm.db.conn.execute(
        "UPDATE projects SET folder='' WHERE id=?",
        (project["id"],),
    )
    pm.db.conn.commit()

    result = repair_safe_project_integrity(pm)
    repaired = pm.db.get_project(project["id"])

    assert repaired["folder"] == str(Path("In Progress") / "Repair Missing Folder Value")
    assert folder.is_dir()
    assert any(item["type"] == "missing_folder_value" for item in result["repaired"])


def test_repair_does_not_recreate_or_move_missing_project_folder(project_manager):
    pm = project_manager
    pm.create_project(
        title="Manual Review Missing Folder",
        category="Testing",
        status="Completed",
    )
    project = _project_by_title(pm, "Manual Review Missing Folder")
    folder = pm.resolve_project_folder(project)
    folder.rename(folder.with_name("Moved Somewhere Else"))

    result = repair_safe_project_integrity(pm)
    unchanged = pm.db.get_project(project["id"])

    assert unchanged["folder"] == str(Path("Completed") / "Manual Review Missing Folder")
    assert not folder.exists()
    assert any(item["type"] == "missing_folder" for item in result["skipped"])
