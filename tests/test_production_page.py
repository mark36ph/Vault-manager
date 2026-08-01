from pages.production_page import (
    production_settings_from_app,
    project_choice,
    selected_asset_providers,
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
