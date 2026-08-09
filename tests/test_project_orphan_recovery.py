from datetime import datetime, timedelta
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


def test_recover_scheduled_orphan_requires_future_schedule(project_manager):
    pm = project_manager
    orphan = pm.get_projects_root() / "Scheduled" / "Needs Schedule"
    orphan.mkdir(parents=True)

    with pytest.raises(ValueError, match="require a date and time"):
        recover_orphan_project(pm, orphan, category="Misc")

    assert pm.db.get_projects() == []
    assert orphan.exists()


def test_recover_scheduled_orphan_restores_schedule(project_manager):
    pm = project_manager
    orphan = pm.get_projects_root() / "Scheduled" / "Scheduled Recovery"
    orphan.mkdir(parents=True)
    scheduled_for = (datetime.now() + timedelta(days=2)).strftime("%Y-%m-%d %H:%M")

    recovered = recover_orphan_project(
        pm,
        orphan,
        category="History",
        scheduled_for=scheduled_for,
    )

    assert recovered["status"] == "Scheduled"
    assert recovered["scheduled_for"] == scheduled_for
    assert pm.check_project_integrity() == []
