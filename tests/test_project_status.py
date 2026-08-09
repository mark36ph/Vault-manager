from pathlib import Path
from datetime import datetime, timedelta

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


def test_schedule_validation_rejects_blank_invalid_and_past_values(project_manager):
    with pytest.raises(ValueError, match="require a date and time"):
        project_manager._validated_schedule("")

    with pytest.raises(ValueError, match="YYYY-MM-DD HH:MM"):
        project_manager._validated_schedule("tomorrow evening")

    past = (datetime.now() - timedelta(days=1)).strftime("%Y-%m-%d %H:%M")
    with pytest.raises(ValueError, match="future date and time"):
        project_manager._validated_schedule(past)


def test_change_project_status_accepts_valid_future_schedule(project_manager):
    pm = project_manager
    pm.create_project(
        title="Future Schedule Test",
        category="Testing",
        status="In Progress",
    )
    project = next(
        row for row in pm.db.get_projects()
        if row["title"] == "Future Schedule Test"
    )
    scheduled_for = (datetime.now() + timedelta(days=2)).strftime("%Y-%m-%d %H:%M")

    updated = pm.change_project_status(
        project["id"],
        "Scheduled",
        scheduled_for=scheduled_for,
    )

    assert updated["status"] == "Scheduled"
    assert updated["scheduled_for"] == scheduled_for
    assert pm.resolve_project_folder(updated).exists()


def test_completed_project_can_be_scheduled_without_losing_export_files(project_manager):
    pm = project_manager
    pm.create_project(
        title="Completed Schedule Handoff",
        category="Testing",
        status="Completed",
        script="Keep this script",
        description="Keep this description",
        pinned_comment="Keep this comment",
    )
    project = next(
        row for row in pm.db.get_projects()
        if row["title"] == "Completed Schedule Handoff"
    )
    completed_folder = pm.resolve_project_folder(project)
    timeline = completed_folder / "timeline.json"
    package_file = completed_folder / "Export" / "Portable" / "handoff.fcpxml"
    package_file.parent.mkdir(parents=True, exist_ok=True)
    timeline.write_text("timeline", encoding="utf-8")
    package_file.write_text("fcpxml", encoding="utf-8")

    scheduled_for = (datetime.now() + timedelta(days=2)).strftime("%Y-%m-%d %H:%M")
    updated = pm.change_project_status(
        project["id"],
        "Scheduled",
        scheduled_for=scheduled_for,
    )
    scheduled_folder = pm.resolve_project_folder(updated)

    assert updated["status"] == "Scheduled"
    assert updated["scheduled_for"] == scheduled_for
    assert updated["script"] == "Keep this script"
    assert updated["description"] == "Keep this description"
    assert updated["pinned_comment"] == "Keep this comment"
    assert not completed_folder.exists()
    assert (scheduled_folder / "timeline.json").read_text(encoding="utf-8") == "timeline"
    assert (
        scheduled_folder / "Export" / "Portable" / "handoff.fcpxml"
    ).read_text(encoding="utf-8") == "fcpxml"


def test_scheduled_project_publishes_with_artifacts_and_clears_schedule(project_manager):
    pm = project_manager
    pm.create_project(
        title="Scheduled Artifact Handoff",
        category="Testing",
        status="Scheduled",
        script="Published script",
        description="Published description",
    )
    project = next(
        row for row in pm.db.get_projects()
        if row["title"] == "Scheduled Artifact Handoff"
    )
    scheduled_folder = pm.resolve_project_folder(project)
    package_file = scheduled_folder / "Export" / "Portable" / "ready.fcpxml"
    package_file.parent.mkdir(parents=True, exist_ok=True)
    package_file.write_text("ready", encoding="utf-8")
    pm.db.update_project_schedule(project["id"], "2000-01-01 00:00")

    assert pm.complete_due_scheduled_projects() == 1

    published = pm.db.get_project(project["id"])
    published_folder = pm.resolve_project_folder(published)
    assert published["status"] == "Published"
    assert published["scheduled_for"] == ""
    assert published["script"] == "Published script"
    assert published["description"] == "Published description"
    assert not scheduled_folder.exists()
    assert (
        published_folder / "Export" / "Portable" / "ready.fcpxml"
    ).read_text(encoding="utf-8") == "ready"
