from types import SimpleNamespace

from pages.production_page import (
    production_settings_from_app,
    project_choice,
    selected_asset_providers,
    should_mark_project_completed,
)


class Settings:
    def __init__(self, values=None):
        self.values = values or {}

    def get(self, section, key, default=None):
        return self.values.get((section, key), default)


def test_project_choice_shows_title_and_status():
    assert project_choice({"title": "Moon Facts", "status": "In Progress"}) == "Moon Facts  •  In Progress"


def test_project_choice_has_safe_defaults():
    assert project_choice({}) == "Untitled  •  Unknown"


def test_selected_asset_providers_both():
    assert selected_asset_providers(True, True) == ("pexels", "pixabay")


def test_selected_asset_providers_pexels_only():
    assert selected_asset_providers(True, False) == ("pexels",)


def test_selected_asset_providers_pixabay_only():
    assert selected_asset_providers(False, True) == ("pixabay",)


def test_selected_asset_providers_none():
    assert selected_asset_providers(False, False) == ()


def test_production_settings_use_defaults():
    assert production_settings_from_app(Settings()) == {
        "timeline_width": 1080,
        "timeline_height": 1920,
        "frame_rate": 30.0,
    }


def test_production_settings_read_resolve_values():
    settings = Settings({
        ("resolve", "timeline_width"): "2160",
        ("resolve", "timeline_height"): "3840",
        ("resolve", "frame_rate"): "60",
    })
    assert production_settings_from_app(settings) == {
        "timeline_width": 2160,
        "timeline_height": 3840,
        "frame_rate": 60.0,
    }


def test_successful_full_run_marks_in_progress_project_completed():
    state = SimpleNamespace(
        running=False,
        error=None,
        result=object(),
        stages=tuple(SimpleNamespace(status="complete") for _ in range(7)),
    )
    assert should_mark_project_completed(
        {"status": "In Progress"},
        state,
    )


def test_failed_or_partial_run_does_not_mark_project_completed():
    failed = SimpleNamespace(
        running=False,
        error="boom",
        result=None,
        stages=tuple(SimpleNamespace(status="complete") for _ in range(7)),
    )
    partial = SimpleNamespace(
        running=False,
        error=None,
        result=object(),
        stages=(
            SimpleNamespace(status="complete"),
            SimpleNamespace(status="failed"),
        ),
    )

    project = {"status": "In Progress"}
    assert not should_mark_project_completed(project, failed)
    assert not should_mark_project_completed(project, partial)


def test_successful_run_preserves_non_in_progress_status():
    state = SimpleNamespace(
        running=False,
        error=None,
        result=object(),
        stages=tuple(SimpleNamespace(status="complete") for _ in range(7)),
    )
    assert not should_mark_project_completed(
        {"status": "Scheduled"},
        state,
    )


def test_ai_settings_page_is_available():
    from pages.settings.ai_page import AIPage

    assert AIPage.__name__ == "AIPage"
