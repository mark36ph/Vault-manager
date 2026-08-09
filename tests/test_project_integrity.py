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

def test_integrity_detects_missing_folder_value(
    project_manager,
    monkeypatch,
):
    monkeypatch.setattr(
        project_manager.db,
        "get_projects",
        lambda: [
            {
                "id": 1,
                "title": "Missing Folder Project",
                "status": "Draft",
                "folder": "",
            }
        ],
    )

    issues = project_manager.check_project_integrity()

    assert any(
        issue["type"] == "missing_folder_value"
        and issue["project_id"] == 1
        for issue in issues
    )
    
def test_integrity_detects_absolute_folder_path(
    project_manager,
    monkeypatch,
):
    absolute_folder = (
        project_manager.get_projects_root()
        / "Draft"
        / "Absolute Path Project"
    )
    absolute_folder.mkdir(parents=True)

    monkeypatch.setattr(
        project_manager.db,
        "get_projects",
        lambda: [
            {
                "id": 1,
                "title": "Absolute Path Project",
                "status": "Draft",
                "folder": str(absolute_folder.resolve()),
            }
        ],
    )

    issues = project_manager.check_project_integrity()

    assert any(
        issue["type"] == "absolute_path"
        and issue["project_id"] == 1
        for issue in issues
    )

def test_integrity_detects_folder_outside_projects_root(
    project_manager,
    tmp_path,
    monkeypatch,
):
    outside_folder = (
        tmp_path
        / "Outside"
        / "External Project"
    )
    outside_folder.mkdir(parents=True)

    monkeypatch.setattr(
        project_manager.db,
        "get_projects",
        lambda: [
            {
                "id": 1,
                "title": "External Project",
                "status": "Draft",
                "folder": str(outside_folder.resolve()),
            }
        ],
    )

    issues = project_manager.check_project_integrity()

    assert any(
        issue["type"] == "outside_projects_root"
        and issue["project_id"] == 1
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


def test_integrity_detects_missing_schedule(project_manager):
    pm = project_manager
    pm.create_project(
        title="Missing Schedule",
        category="Testing",
        status="Scheduled",
    )

    issues = pm.check_project_integrity()

    assert any(
        issue["type"] == "missing_schedule"
        and issue["title"] == "Missing Schedule"
        for issue in issues
    )


def test_integrity_detects_invalid_schedule(project_manager):
    pm = project_manager
    pm.create_project(
        title="Invalid Schedule",
        category="Testing",
        status="Scheduled",
    )
    project = next(
        row for row in pm.db.get_projects()
        if row["title"] == "Invalid Schedule"
    )
    pm.db.update_project_schedule(project["id"], "not-a-date")

    issues = pm.check_project_integrity()

    assert any(
        issue["type"] == "invalid_schedule"
        and issue["scheduled_for"] == "not-a-date"
        for issue in issues
    )


def test_integrity_detects_stale_schedule_on_non_scheduled_project(project_manager):
    pm = project_manager
    pm.create_project(
        title="Stale Schedule",
        category="Testing",
        status="Completed",
    )
    project = next(
        row for row in pm.db.get_projects()
        if row["title"] == "Stale Schedule"
    )
    pm.db.update_project_schedule(project["id"], "2099-01-01 12:00")

    issues = pm.check_project_integrity()

    assert any(
        issue["type"] == "stale_schedule"
        and issue["title"] == "Stale Schedule"
        for issue in issues
    )
