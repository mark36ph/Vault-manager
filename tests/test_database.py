from pathlib import Path


def test_created_project_is_stored_with_all_fields(project_manager):
    pm = project_manager

    pm.create_project(
        title="Database Test",
        category="Science",
        status="In Progress",
        script="Test script",
        description="Test description",
        pinned_comment="Test pinned comment",
        notes="Test notes",
    )

    project = next(
        p
        for p in pm.db.get_projects()
        if p["title"] == "Database Test"
    )

    assert project["title"] == "Database Test"
    assert project["category"] == "Science"
    assert project["status"] == "In Progress"

    assert project["script"] == "Test script"
    assert project["description"] == "Test description"
    assert project["pinned_comment"] == "Test pinned comment"
    assert project["notes"] == "Test notes"

    stored_folder = Path(project["folder"])

    assert not stored_folder.is_absolute()
    assert stored_folder == (
        Path("In Progress") / "Database Test"
    )

def test_updating_project_preserves_other_fields(project_manager):
    pm = project_manager

    pm.create_project(
        title="Update Test",
        category="Science",
        status="In Progress",
        script="Original script",
        description="Original description",
        pinned_comment="Original pinned comment",
        notes="Original notes",
    )

    project = next(
        p
        for p in pm.db.get_projects()
        if p["title"] == "Update Test"
    )

    pm.db.update_project(
        project["id"],
        title="Updated Title",
        category="Science",
        status="In Progress",
        folder=project["folder"],
        script="Updated script",
        description="Original description",
        pinned_comment="Original pinned comment",
        notes="Original notes",
    )

    updated = pm.db.get_project(project["id"])

    assert updated["title"] == "Updated Title"
    assert updated["script"] == "Updated script"

    assert updated["description"] == "Original description"
    assert updated["pinned_comment"] == "Original pinned comment"
    assert updated["notes"] == "Original notes"
    assert updated["status"] == "In Progress"