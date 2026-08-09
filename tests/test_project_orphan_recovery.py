from pathlib import Path

import pytest

from common.project_orphan_recovery import recover_orphan_project


def test_recover_orphan_project_links_existing_folder_and_imports_legacy_text(project_manager):
    pm = project_manager
    orphan = pm.get_projects_root() / "Completed" / "Recovered Fact"
    orphan.mkdir(parents=True)
    (orphan / "Script.txt").write_text("Recovered script", encoding="utf-8")
    (orphan / "Description.txt").write_text("Recovered description", encoding="utf-8")

    recovered = recover_orphan_project(pm, orphan, category="Science")

    assert recovered["title"] == "Recovered Fact"
    assert recovered["status"] == "Completed"
    assert recovered["category"] == "Science"
    assert recovered["folder"] == str(Path("Completed") / "Recovered Fact")
    assert recovered["script"] == "Recovered script"
    assert recovered["description"] == "Recovered description"
    assert orphan.exists()
    assert pm.check_project_integrity() == []


def test_recover_orphan_project_rejects_folder_already_linked(project_manager):
    pm = project_manager
    pm.create_project(
        title="Already Linked",
        category="Testing",
        status="Completed",
    )
    project = next(row for row in pm.db.get_projects() if row["title"] == "Already Linked")

    with pytest.raises(ValueError, match="already linked"):
        recover_orphan_project(pm, pm.resolve_project_folder(project), category="Testing")


def test_recover_orphan_project_rejects_nested_folder(project_manager):
    pm = project_manager
    nested = pm.get_projects_root() / "Completed" / "Outer" / "Inner"
    nested.mkdir(parents=True)

    with pytest.raises(ValueError, match="directly inside a status folder"):
        recover_orphan_project(pm, nested, category="Misc")


def test_recover_scheduled_orphan_defaults_to_in_progress(project_manager):
    pm = project_manager
    orphan = pm.get_projects_root() / "Scheduled" / "Needs Recovery"
    orphan.mkdir(parents=True)
    marker = orphan / "keep.txt"
    marker.write_text("keep", encoding="utf-8")

    recovered = recover_orphan_project(pm, orphan, category="Misc")
    recovered_folder = pm.resolve_project_folder(recovered)

    assert recovered["status"] == "In Progress"
    assert recovered["scheduled_for"] == ""
    assert recovered["folder"] == str(Path("In Progress") / "Needs Recovery")
    assert not orphan.exists()
    assert (recovered_folder / "keep.txt").read_text(encoding="utf-8") == "keep"
    assert pm.check_project_integrity() == []


def test_recover_scheduled_orphan_can_be_recovered_as_completed(project_manager):
    pm = project_manager
    orphan = pm.get_projects_root() / "Scheduled" / "Completed Recovery"
    orphan.mkdir(parents=True)

    recovered = recover_orphan_project(
        pm,
        orphan,
        category="History",
        target_status="Completed",
    )

    assert recovered["status"] == "Completed"
    assert recovered["scheduled_for"] == ""
    assert recovered["folder"] == str(Path("Completed") / "Completed Recovery")
    assert pm.resolve_project_folder(recovered).exists()
    assert not orphan.exists()
    assert pm.check_project_integrity() == []


def test_recover_orphan_rejects_scheduled_target(project_manager):
    pm = project_manager
    orphan = pm.get_projects_root() / "Scheduled" / "No Reschedule"
    orphan.mkdir(parents=True)

    with pytest.raises(ValueError, match="cannot be returned to Scheduled"):
        recover_orphan_project(
            pm,
            orphan,
            category="Misc",
            target_status="Scheduled",
        )

    assert orphan.exists()
    assert pm.db.get_projects() == []


def test_recover_orphan_restores_original_folder_if_database_insert_fails(
    project_manager,
    monkeypatch,
):
    pm = project_manager
    orphan = pm.get_projects_root() / "Scheduled" / "Rollback Recovery"
    orphan.mkdir(parents=True)

    def fail_add_project(*args, **kwargs):
        raise RuntimeError("database insert failed")

    monkeypatch.setattr(pm.db, "add_project", fail_add_project)

    with pytest.raises(RuntimeError, match="database insert failed"):
        recover_orphan_project(
            pm,
            orphan,
            category="Misc",
            target_status="In Progress",
        )

    assert orphan.exists()
    assert not (pm.get_projects_root() / "In Progress" / "Rollback Recovery").exists()
