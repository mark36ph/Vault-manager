from pathlib import Path


def test_create_project_creates_required_folders(project_manager):
    pm = project_manager

    pm.create_project(
        title="Folder Test",
        category="Testing",
        status="In Progress",
    )

    project = next(
        p
        for p in pm.db.get_projects()
        if p["title"] == "Folder Test"
    )

    folder = pm.resolve_project_folder(project)

    expected = [
        folder,
        folder / "Assets",
        folder / "Assets" / "Images",
        folder / "Assets" / "Videos",
        folder / "Assets" / "Music",
        folder / "Assets" / "SFX",
        folder / "Assets" / "Overlays",
        folder / "Assets" / "Thumbnails",
        folder / "CapCut",
        folder / "Export",
        folder / "Voice",
    ]

    for path in expected:
        assert path.exists(), f"Missing folder: {path}"