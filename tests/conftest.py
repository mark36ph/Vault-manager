import pytest

from database import Database
from project_manager import ProjectManager


@pytest.fixture
def project_manager(tmp_path, monkeypatch):
    test_db = Database(
        db_path=tmp_path / "factvault_test.db"
    )

    pm = ProjectManager(db=test_db)

    test_projects_root = tmp_path / "Projects"
    test_projects_root.mkdir()

    monkeypatch.setattr(
        pm,
        "get_projects_root",
        lambda: test_projects_root,
    )

    yield pm

    pm.db.conn.close()