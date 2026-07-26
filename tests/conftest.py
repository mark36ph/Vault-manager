import pytest

from database import Database
from project_manager import ProjectManager


class TestSettings:
    def __init__(self, projects_root):
        self.projects_root = projects_root

    def section(self, name):
        if name == "general":
            return {
                "projects_folder": str(self.projects_root),
            }

        return {}


@pytest.fixture
def project_manager(tmp_path):
    test_db = Database(
        db_path=tmp_path / "factvault_test.db"
    )

    test_projects_root = tmp_path / "Projects"
    test_projects_root.mkdir()

    test_settings = TestSettings(test_projects_root)

    pm = ProjectManager(
        db=test_db,
        settings=test_settings,
    )

    yield pm

    pm.close()

@pytest.fixture
def database(tmp_path):
    db = Database(
        db_path=tmp_path / "factvault_test.db"
    )

    yield db

    db.close()