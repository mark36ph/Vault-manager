from pathlib import Path

import pytest


def test_status_change_restores_folder_when_database_update_fails(
    project_manager,
    monkeypatch,
):
    project_manager.create_project(
        title="Rollback Project",
        category="Test",
        status="Draft",
    )

    project = next(
        project
        for project in project_manager.db.get_projects()
        if project["title"] == "Rollback Project"
    )

    project_id = project["id"]
    old_folder = project_manager.resolve_project_folder(project)

    new_folder = (
        project_manager.get_projects_root()
        / "Published"
        / "Rollback Project"
    )

    def fail_database_update(*args, **kwargs):
        raise RuntimeError("Simulated database failure")

    monkeypatch.setattr(
        project_manager.db,
        "update_project_status_and_folder",
        fail_database_update,
    )

    with pytest.raises(
        RuntimeError,
        match="Simulated database failure",
    ):
        project_manager.change_project_status(
            project_id,
            "Published",
        )

    assert old_folder.exists()
    assert not new_folder.exists()

    unchanged = project_manager.db.get_project(project_id)

    assert unchanged["status"] == "Draft"
    assert unchanged["folder"] == str(
        Path("Draft") / "Rollback Project"
    )

def test_complete_due_projects_continues_after_failure(
    project_manager,
    monkeypatch,
    capsys,
):
    monkeypatch.setattr(
        project_manager.db,
        "get_due_scheduled_project_ids",
        lambda: [101, 102, 103],
    )

    completed = []

    def fake_change_status(
        project_id,
        new_status,
        scheduled_for="",
    ):
        if project_id == 102:
            raise RuntimeError("Publish failed")

        completed.append(project_id)

    monkeypatch.setattr(
        project_manager,
        "change_project_status",
        fake_change_status,
    )

    result = project_manager.complete_due_scheduled_projects()

    assert result == 2
    assert completed == [101, 103]

    output = capsys.readouterr().out
    assert "Could not publish scheduled project 102" in output
    assert "Publish failed" in output
    

def test_change_project_status_moves_folder_and_updates_database(
    project_manager,
):
    pm = project_manager

    pm.create_project(
        title="Status Move Test",
        category="Testing",
        status="In Progress",
    )

    project = next(
        project
        for project in pm.db.get_projects()
        if project["title"] == "Status Move Test"
    )

    old_folder = pm.resolve_project_folder(project)

    assert old_folder.exists()

    updated_project = pm.change_project_status(
        project_id=project["id"],
        new_status="Completed",
    )

    new_folder = pm.resolve_project_folder(updated_project)

    assert updated_project["status"] == "Completed"
    assert updated_project["folder"] == str(
        Path("Completed") / "Status Move Test"
    )

    assert new_folder.exists()
    assert not old_folder.exists()

def test_change_project_status_fails_safely_when_destination_exists(
    project_manager,
):
    pm = project_manager

    pm.create_project(
        title="Existing Destination Test",
        category="Testing",
        status="In Progress",
    )

    project = next(
        project
        for project in pm.db.get_projects()
        if project["title"] == "Existing Destination Test"
    )

    old_folder = pm.resolve_project_folder(project)

    destination_folder = (
        pm.get_projects_root()
        / "Completed"
        / "Existing Destination Test"
    )
    destination_folder.mkdir(parents=True)

    with pytest.raises(FileExistsError):
        pm.change_project_status(
            project_id=project["id"],
            new_status="Completed",
        )

    refreshed_project = pm.db.get_project(project["id"])

    assert refreshed_project["status"] == "In Progress"
    assert refreshed_project["folder"] == str(
        Path("In Progress") / "Existing Destination Test"
    )

    assert old_folder.exists()
    assert destination_folder.exists()

def test_due_scheduled_project_moves_to_published(
    project_manager,
):
    pm = project_manager

    pm.create_project(
        title="Scheduled Publish Test",
        category="Testing",
        status="Scheduled",
    )

    project = next(
        project
        for project in pm.db.get_projects()
        if project["title"] == "Scheduled Publish Test"
    )

    pm.db.update_project_schedule(
        project["id"],
        "2000-01-01 00:00",
    )

    old_folder = pm.resolve_project_folder(project)

    changed_count = pm.complete_due_scheduled_projects()

    refreshed_project = pm.db.get_project(project["id"])
    new_folder = pm.resolve_project_folder(refreshed_project)

    assert changed_count == 1
    assert refreshed_project["status"] == "Published"
    assert refreshed_project["folder"] == str(
        Path("Published") / "Scheduled Publish Test"
    )
    assert refreshed_project["scheduled_for"] == ""

    assert new_folder.exists()
    assert not old_folder.exists()