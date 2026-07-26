def get_project_by_title(pm, title):
    return next(
        project
        for project in pm.db.get_projects()
        if project["title"] == title
    )


def set_project_fields(pm, project_id, **fields):
    assignments = ", ".join(
        f"{column}=?" for column in fields
    )
    values = list(fields.values()) + [project_id]

    pm.db.conn.execute(
        f"""
        UPDATE projects
        SET {assignments}
        WHERE id=?
        """,
        values,
    )
    pm.db.conn.commit()


def test_migrate_legacy_project_files_imports_empty_fields(
    project_manager,
):
    pm = project_manager

    pm.create_project(
        title="Legacy Project",
        category="Testing",
        status="Draft",
    )

    project = get_project_by_title(
        pm,
        "Legacy Project",
    )
    project_folder = pm.resolve_project_folder(project)

    (project_folder / "Script.txt").write_text(
        "Imported script",
        encoding="utf-8",
    )
    (project_folder / "Notes.txt").write_text(
        "Imported notes",
        encoding="utf-8",
    )
    (project_folder / "Tags.txt").write_text(
        "history, science",
        encoding="utf-8",
    )

    # The legacy migration expects the stored folder to be
    # directly resolvable by pathlib.
    set_project_fields(
        pm,
        project["id"],
        folder=str(project_folder),
    )

    pm.db.migrate_legacy_project_files()

    migrated = pm.db.get_project(project["id"])

    assert migrated["script"] == "Imported script"
    assert migrated["notes"] == "Imported notes"
    assert migrated["tags"] == "history, science"


def test_migrate_legacy_project_files_preserves_existing_values(
    project_manager,
):
    pm = project_manager

    pm.create_project(
        title="Existing Value Project",
        category="Testing",
        status="Draft",
        notes="Current database notes",
    )

    project = get_project_by_title(
        pm,
        "Existing Value Project",
    )
    project_folder = pm.resolve_project_folder(project)

    (project_folder / "Notes.txt").write_text(
        "Legacy file notes",
        encoding="utf-8",
    )

    set_project_fields(
        pm,
        project["id"],
        folder=str(project_folder),
    )

    pm.db.migrate_legacy_project_files()

    migrated = pm.db.get_project(project["id"])

    assert migrated["notes"] == "Current database notes"


def test_migrate_legacy_project_files_ignores_empty_files(
    project_manager,
):
    pm = project_manager

    pm.create_project(
        title="Empty Legacy File Project",
        category="Testing",
        status="Draft",
    )

    project = get_project_by_title(
        pm,
        "Empty Legacy File Project",
    )
    project_folder = pm.resolve_project_folder(project)

    (project_folder / "Sources.txt").write_text(
        "   ",
        encoding="utf-8",
    )

    set_project_fields(
        pm,
        project["id"],
        folder=str(project_folder),
    )

    pm.db.migrate_legacy_project_files()

    migrated = pm.db.get_project(project["id"])

    assert not migrated["sources"]


def test_migrate_legacy_project_files_skips_missing_folder(
    project_manager,
    tmp_path,
):
    pm = project_manager

    pm.create_project(
        title="Missing Folder Project",
        category="Testing",
        status="Draft",
    )

    project = get_project_by_title(
        pm,
        "Missing Folder Project",
    )
    missing_folder = tmp_path / "Does Not Exist"

    set_project_fields(
        pm,
        project["id"],
        folder=str(missing_folder),
    )

    pm.db.migrate_legacy_project_files()

    migrated = pm.db.get_project(project["id"])

    assert not migrated["script"]
    assert not migrated["notes"]


def test_migrate_legacy_project_files_skips_empty_folder_value(
    project_manager,
):
    pm = project_manager

    pm.create_project(
        title="No Folder Project",
        category="Testing",
        status="Draft",
    )

    project = get_project_by_title(
        pm,
        "No Folder Project",
    )

    set_project_fields(
        pm,
        project["id"],
        folder="",
    )

    pm.db.migrate_legacy_project_files()

    migrated = pm.db.get_project(project["id"])

    assert not migrated["script"]
    assert not migrated["notes"]