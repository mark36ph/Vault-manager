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