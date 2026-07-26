def get_project_by_title(pm, title):
    return next(
        project
        for project in pm.db.get_projects()
        if project["title"] == title
    )


def test_toggle_project_pinned_marks_unpinned_project_as_pinned(
    project_manager,
):
    pm = project_manager

    pm.create_project(
        title="Pin Project",
        category="Testing",
        status="Draft",
    )

    project = get_project_by_title(
        pm,
        "Pin Project",
    )

    pm.db.toggle_project_pinned(project["id"])

    updated = pm.db.get_project(project["id"])

    assert updated["pinned"] == 1


def test_toggle_project_pinned_marks_pinned_project_as_unpinned(
    project_manager,
):
    pm = project_manager

    pm.create_project(
        title="Unpin Project",
        category="Testing",
        status="Draft",
    )

    project = get_project_by_title(
        pm,
        "Unpin Project",
    )

    pm.db.toggle_project_pinned(project["id"])
    pm.db.toggle_project_pinned(project["id"])

    updated = pm.db.get_project(project["id"])

    assert updated["pinned"] == 0


def test_toggle_project_pinned_ignores_missing_project(
    project_manager,
):
    pm = project_manager

    before = pm.db.conn.total_changes

    pm.db.toggle_project_pinned(999999)

    after = pm.db.conn.total_changes

    assert after == before