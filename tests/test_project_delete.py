import shutil


def test_delete_project_removes_folder_and_database_record(project_manager):
    pm = project_manager

    pm.create_project(
        title="Delete Test",
        category="Testing",
        status="Completed",
    )

    project = next(
        p
        for p in pm.db.get_projects()
        if p["title"] == "Delete Test"
    )

    folder = pm.resolve_project_folder(project)

    assert folder.exists()

    pm.delete_project(project["id"])

    assert pm.db.get_project(project["id"]) is None
    assert not folder.exists()


def test_delete_project_removes_folder_and_database_record(project_manager):
    pm = project_manager

    pm.create_project(
        title="Delete Test",
        category="Testing",
        status="Completed",
    )

    project = next(
        p
        for p in pm.db.get_projects()
        if p["title"] == "Delete Test"
    )

    folder = pm.resolve_project_folder(project)

    assert folder.exists()

    pm.delete_project(project["id"])

    assert pm.db.get_project(project["id"]) is None
    assert not folder.exists()


def test_delete_project_handles_missing_folder(project_manager):
    pm = project_manager

    pm.create_project(
        title="Missing Folder Delete Test",
        category="Testing",
        status="Completed",
    )

    project = next(
        p
        for p in pm.db.get_projects()
        if p["title"] == "Missing Folder Delete Test"
    )

    folder = pm.resolve_project_folder(project)

    # Simulate the folder already having been deleted
    shutil.rmtree(folder)

    result = pm.delete_project(project["id"])

    assert result is True
    assert pm.db.get_project(project["id"]) is None