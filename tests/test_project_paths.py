from pathlib import Path

import pytest


def test_get_projects_root_returns_absolute_path(
    project_manager,
    tmp_path,
):
    project_manager.settings.section(
        "general"
    )["projects_folder"] = str(tmp_path / "Projects")

    result = project_manager.get_projects_root()

    assert result == (tmp_path / "Projects").resolve()
    assert result.is_absolute()


def test_get_projects_root_raises_when_not_configured(
    project_manager,
):
    project_manager.settings.projects_root = ""

    with pytest.raises(
        Exception,
        match="Please select your Projects Folder",
    ):
        project_manager.get_projects_root()

def test_resolve_project_folder_returns_absolute_stored_path(
    project_manager,
    tmp_path,
):
    absolute_folder = (tmp_path / "Legacy Project").resolve()

    project = {
        "title": "Legacy Project",
        "status": "Draft",
        "folder": str(absolute_folder),
    }

    result = project_manager.resolve_project_folder(project)

    assert result == absolute_folder


def test_resolve_project_folder_resolves_relative_stored_path(
    project_manager,
    tmp_path,
):
    projects_root = tmp_path / "Projects"

    project_manager.settings.section(
        "general"
    )["projects_folder"] = str(projects_root)

    project = {
        "title": "Test Project",
        "status": "Published",
        "folder": str(Path("Published") / "Test Project"),
    }

    result = project_manager.resolve_project_folder(project)

    assert result == (
        projects_root
        / "Published"
        / "Test Project"
    ).resolve()


def test_resolve_project_folder_falls_back_to_status_and_title(
    project_manager,
    tmp_path,
):
    projects_root = tmp_path / "Projects"

    project_manager.settings.section(
        "general"
    )["projects_folder"] = str(projects_root)

    project = {
        "title": "Test Project",
        "status": "Draft",
        "folder": "",
    }

    result = project_manager.resolve_project_folder(project)

    assert result == (
        projects_root
        / "Draft"
        / "Test Project"
    ).resolve()


def test_get_relative_project_folder_returns_relative_path(
    project_manager,
    tmp_path,
):
    projects_root = tmp_path / "Projects"

    project_manager.settings.section(
        "general"
    )["projects_folder"] = str(projects_root)

    project_folder = (
        projects_root
        / "Scheduled"
        / "Test Project"
    )

    result = project_manager.get_relative_project_folder(
        project_folder
    )

    assert result == Path(
        "Scheduled"
    ) / "Test Project"


def test_get_relative_project_folder_rejects_external_path(
    project_manager,
    tmp_path,
):
    projects_root = tmp_path / "Projects"
    external_folder = tmp_path / "Outside" / "Test Project"

    project_manager.settings.section(
        "general"
    )["projects_folder"] = str(projects_root)

    with pytest.raises(
        Exception,
        match="must be inside the configured Projects folder",
    ):
        project_manager.get_relative_project_folder(
            external_folder
        )


def test_get_voice_folder_creates_voice_directory(
    project_manager,
    tmp_path,
):
    projects_root = tmp_path / "Projects"

    project_manager.settings.section(
        "general"
    )["projects_folder"] = str(projects_root)

    project = {
        "title": "Test Project",
        "status": "Draft",
        "folder": str(Path("Draft") / "Test Project"),
    }

    voice_folder = project_manager.get_voice_folder(project)

    assert voice_folder == (
        projects_root
        / "Draft"
        / "Test Project"
        / "Voice"
    ).resolve()

    assert voice_folder.is_dir()