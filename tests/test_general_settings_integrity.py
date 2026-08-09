from pages.settings.general_page import integrity_report_text, split_integrity_issues


def test_split_integrity_issues_separates_safe_and_manual_findings():
    issues = [
        {"type": "stale_schedule", "title": "Completed Fact"},
        {"type": "absolute_path", "title": "Old Path"},
        {"type": "missing_folder", "title": "Missing Project"},
        {"type": "status_folder_mismatch", "title": "Wrong Folder"},
    ]

    safe, manual = split_integrity_issues(issues)

    assert [issue["type"] for issue in safe] == ["stale_schedule", "absolute_path"]
    assert [issue["type"] for issue in manual] == [
        "missing_folder",
        "status_folder_mismatch",
    ]


def test_integrity_report_labels_safe_and_manual_findings():
    report = integrity_report_text(
        [
            {
                "type": "stale_schedule",
                "title": "Completed Fact",
                "message": "Non-scheduled project still has a scheduled date and time.",
            },
            {
                "type": "orphan_folder",
                "folder": "C:/Projects/Completed/Orphan",
                "message": "This folder is not linked to any database project.",
            },
        ]
    )

    assert "1 safe repair candidate(s)" in report
    assert "1 manual review issue(s)" in report
    assert "[SAFE REPAIR] Completed Fact" in report
    assert "[MANUAL REVIEW] C:/Projects/Completed/Orphan" in report


def test_integrity_report_is_clear_when_no_issues_exist():
    assert integrity_report_text([]) == "No project integrity issues found."
