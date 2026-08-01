from pages.production_page import format_elapsed, progress_percent


def test_format_elapsed_under_one_hour():
    assert format_elapsed(0) == "00:00"
    assert format_elapsed(65.9) == "01:05"


def test_format_elapsed_with_hours_and_negative_values():
    assert format_elapsed(3661) == "1:01:01"
    assert format_elapsed(-5) == "00:00"


def test_progress_percent_clamps_and_rounds():
    assert progress_percent(-1) == "0%"
    assert progress_percent(0.644) == "64%"
    assert progress_percent(1.5) == "100%"
