import shutil


def test_project_integrity_detects_missing_folder(project_manager):
    pm = project_manager

    pm.create_project(
        title="Missing Folder Integrity Test",
        category="Testing",
        status="Completed",
    )

    project = next(
        p
        for p in pm.db.get_projects()
        if p["title"] == "Missing Folder Integrity Test"
    )

    folder = pm.resolve_project_folder(project)
    shutil.rmtree(folder)

    issues = pm.check_project_integrity()

    assert any(
        "Missing Folder Integrity Test" in str(issue)
        for issue in issues
    )

def test_project_integrity_reports_no_issues_for_valid_project(
    project_manager,
):
    pm = project_manager

    pm.create_project(
        title="Integrity Test",
        category="Testing",
        status="In Progress",
    )

    issues = pm.check_project_integrity()

    assert issues == []