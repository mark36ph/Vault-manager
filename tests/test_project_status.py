from pathlib import Path
from database import Database
import pytest
from project_manager import ProjectManager


@pytest.fixture
def project_manager(tmp_path, monkeypatch):
    test_db_path = tmp_path / "factvault_test.db"
    test_db = Database(db_path=test_db_path)

    pm = ProjectManager(db=test_db)

    test_projects_root = tmp_path / "Projects"
    test_projects_root.mkdir()

    monkeypatch.setattr(
        pm,
        "get_projects_root",
        lambda: test_projects_root,
    )

    yield pm

    pm.db.conn.close()


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