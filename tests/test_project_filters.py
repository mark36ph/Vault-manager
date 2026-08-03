from common.project_filters import in_progress_projects


def test_in_progress_projects_filters_other_statuses():
    projects = [
        {"id": 1, "title": "A", "status": "In Progress"},
        {"id": 2, "title": "B", "status": "Completed"},
        {"id": 3, "title": "C", "status": "Idea"},
    ]
    assert [project["id"] for project in in_progress_projects(projects)] == [1]


def test_in_progress_projects_is_case_and_whitespace_tolerant():
    projects = [
        {"id": 1, "status": " in progress "},
        {"id": 2, "status": "IN PROGRESS"},
    ]
    assert [project["id"] for project in in_progress_projects(projects)] == [1, 2]


def test_in_progress_projects_returns_dictionary_copies():
    source = {"id": 1, "status": "In Progress"}
    result = in_progress_projects([source])
    assert result == [source]
    assert result[0] is not source
