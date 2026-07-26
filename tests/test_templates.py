from pathlib import Path


def test_apply_template_does_nothing_when_template_is_missing(
    project_manager,
    tmp_path,
    monkeypatch,
):
    pm = project_manager

    monkeypatch.chdir(tmp_path)

    project_folder = tmp_path / "Project"
    project_folder.mkdir()

    pm.apply_template(
        project_folder=project_folder,
        template="Missing Template",
    )

    assert list(project_folder.iterdir()) == []


def test_apply_template_copies_files_and_directories(
    project_manager,
    tmp_path,
    monkeypatch,
):
    pm = project_manager

    monkeypatch.chdir(tmp_path)

    template_folder = (
        tmp_path
        / "templates"
        / "Test Template"
    )
    nested_folder = template_folder / "Assets"

    nested_folder.mkdir(parents=True)

    template_file = template_folder / "instructions.txt"
    template_file.write_text(
        "Template instructions",
        encoding="utf-8",
    )

    nested_file = nested_folder / "example.txt"
    nested_file.write_text(
        "Nested template file",
        encoding="utf-8",
    )

    project_folder = tmp_path / "Project"
    project_folder.mkdir()

    pm.apply_template(
        project_folder=project_folder,
        template="Test Template",
    )

    copied_file = project_folder / "instructions.txt"
    copied_nested_file = (
        project_folder
        / "Assets"
        / "example.txt"
    )

    assert copied_file.exists()
    assert copied_file.read_text(
        encoding="utf-8"
    ) == "Template instructions"

    assert copied_nested_file.exists()
    assert copied_nested_file.read_text(
        encoding="utf-8"
    ) == "Nested template file"

def test_get_templates_returns_standard_fact_when_folder_missing(
    project_manager,
    tmp_path,
    monkeypatch,
):
    monkeypatch.chdir(tmp_path)

    templates = project_manager.get_templates()

    assert templates == ["Standard Fact"]


def test_get_templates_returns_sorted_template_names(
    project_manager,
    tmp_path,
    monkeypatch,
):
    monkeypatch.chdir(tmp_path)

    templates_folder = tmp_path / "templates"
    templates_folder.mkdir()

    (templates_folder / "zebra").mkdir()
    (templates_folder / "Alpha").mkdir()
    (templates_folder / "beta").mkdir()

    # Files should be ignored.
    (templates_folder / "notes.txt").write_text(
        "Not a template",
        encoding="utf-8",
    )

    templates = project_manager.get_templates()

    assert templates == [
        "Alpha",
        "beta",
        "zebra",
    ]


def test_get_templates_returns_standard_fact_when_folder_empty(
    project_manager,
    tmp_path,
    monkeypatch,
):
    monkeypatch.chdir(tmp_path)

    (tmp_path / "templates").mkdir()

    templates = project_manager.get_templates()

    assert templates == ["Standard Fact"]

def close(self):
    self.db.close()

def count_projects_by_status(self, status):
    return self.db.count_projects_by_status(status)