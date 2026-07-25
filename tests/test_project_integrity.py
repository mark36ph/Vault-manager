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

def test_project_integrity_detects_status_folder_mismatch(project_manager):
    pm = project_manager

    pm.create_project(
        title="Mismatch Test",
        category="Testing",
        status="In Progress",
    )

    project = next(
        p
        for p in pm.db.get_projects()
        if p["title"] == "Mismatch Test"
    )

    original_folder = pm.resolve_project_folder(project)

    wrong_folder = (
        pm.get_projects_root()
        / "Completed"
        / "Mismatch Test"
    )
    wrong_folder.parent.mkdir(parents=True, exist_ok=True)
    original_folder.rename(wrong_folder)

    issues = pm.check_project_integrity()

    assert any(
        "Mismatch Test" in str(issue)
        and "mismatch" in str(issue).lower()
        for issue in issues
    )

def test_project_integrity_detects_orphan_folder(project_manager):
    pm = project_manager

    orphan_folder = (
        pm.get_projects_root()
        / "Completed"
        / "Orphan Project"
    )
    orphan_folder.mkdir(parents=True)

    issues = pm.check_project_integrity()

    assert any(
        "Orphan Project" in str(issue)
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