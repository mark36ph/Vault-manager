from pathlib import Path

import pytest

from common.project_orphan_deletion import delete_orphan_project


def test_delete_orphan_project_removes_unlinked_folder(project_manager):
    pm = project_manager
    orphan = pm.get_projects_root() / "Completed" / "Delete Me"
    orphan.mkdir(parents=True)
    (orphan / "keep-nothing.txt").write_text("test", encoding="utf-8")

    deleted = delete_orphan_project(pm, orphan)

    assert deleted == orphan.resolve()
    assert not orphan.exists()
    assert pm.check_project_integrity() == []


def test_delete_orphan_project_rejects_linked_project(project_manager):
    pm = project_manager
    pm.create_project(
        title="Linked Project",
        category="Testing",
        status="Completed",
    )
    project = next(
        row for row in pm.db.get_projects()
        if row["title"] == "Linked Project"
    )
    folder = pm.resolve_project_folder(project)

    with pytest.raises(ValueError, match="linked to a database project"):
        delete_orphan_project(pm, folder)

    assert folder.exists()
    assert pm.db.get_project(project["id"]) is not None


def test_delete_orphan_project_rejects_nested_folder(project_manager):
    pm = project_manager
    nested = pm.get_projects_root() / "Completed" / "Outer" / "Inner"
    nested.mkdir(parents=True)

    with pytest.raises(ValueError, match="directly inside a status folder"):
        delete_orphan_project(pm, nested)

    assert nested.exists()
