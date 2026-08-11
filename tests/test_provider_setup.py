import json
from types import SimpleNamespace

import pytest

from common.provider_setup import (
    ProviderCredentials,
    ProviderSettings,
    ProviderSettingsStore,
    ProviderSetupError,
    _imported_scene_searches,
    build_configured_providers,
    credentials_from_app_settings,
    test_provider_credentials as credential_statuses,
)


def credentials(**overrides):
    values = {
        "OPENAI_API_KEY": "openai-key",
        "PEXELS_API_KEY": "pexels-key",
        "PIXABAY_API_KEY": "pixabay-key",
    }
    values.update(overrides)
    return ProviderCredentials(values)


class AppSettings:
    def __init__(self, values=None):
        self.values = values or {}

    def get(self, section, key, default=None):
        return self.values.get((section, key), default)


def test_default_settings_are_valid():
    settings = ProviderSettings()
    settings.validate()
    assert settings.asset_providers == ("pexels", "pixabay")


def test_settings_reject_unknown_text_provider():
    with pytest.raises(ValueError, match="unsupported text provider"):
        ProviderSettings(text_provider="other").validate()


def test_settings_reject_unknown_voice_provider():
    with pytest.raises(ValueError, match="unsupported voice provider"):
        ProviderSettings(voice_provider="other").validate()


def test_settings_reject_unknown_asset_provider():
    with pytest.raises(ValueError, match="unsupported asset providers"):
        ProviderSettings(asset_providers=("other",)).validate()


def test_settings_reject_invalid_asset_kind():
    with pytest.raises(ValueError, match="asset_kind"):
        ProviderSettings(asset_kind="audio").validate()


def test_settings_reject_invalid_limits():
    with pytest.raises(ValueError, match="asset_limit"):
        ProviderSettings(asset_limit=0).validate()
    with pytest.raises(ValueError, match="asset_attempts"):
        ProviderSettings(asset_attempts=0).validate()


def test_store_returns_defaults_when_file_missing(tmp_path):
    assert ProviderSettingsStore(tmp_path).load() == ProviderSettings()


def test_store_round_trips_settings(tmp_path):
    store = ProviderSettingsStore(tmp_path)
    expected = ProviderSettings(asset_providers=("pixabay",), openai_voice="nova")
    path = store.save(expected)
    assert path.name == "provider_settings.json"
    assert store.load() == expected


def test_store_writes_no_secret_values(tmp_path):
    path = ProviderSettingsStore(tmp_path).save(ProviderSettings())
    text = path.read_text(encoding="utf-8")
    assert "API_KEY" not in text
    assert "openai-key" not in text


def test_store_rejects_invalid_json(tmp_path):
    (tmp_path / "provider_settings.json").write_text("{", encoding="utf-8")
    with pytest.raises(ProviderSetupError, match="could not read"):
        ProviderSettingsStore(tmp_path).load()


def test_store_rejects_non_object_json(tmp_path):
    (tmp_path / "provider_settings.json").write_text("[]", encoding="utf-8")
    with pytest.raises(ProviderSetupError, match="JSON object"):
        ProviderSettingsStore(tmp_path).load()


def test_credentials_require_configured_key():
    with pytest.raises(ProviderSetupError, match="OPENAI_API_KEY"):
        ProviderCredentials({}).get("openai")


def test_credentials_can_report_optional_missing_key():
    assert ProviderCredentials({}).get("pexels", required=False) == ""


def test_credentials_reject_unknown_provider():
    with pytest.raises(ValueError, match="unknown provider"):
        credentials().get("other")


def test_saved_app_credentials_are_used():
    settings = AppSettings({
        ("ai", "api_key"): "saved-openai",
        ("images", "pexels_api_key"): "saved-pexels",
        ("images", "pixabay_api_key"): "saved-pixabay",
    })
    values = credentials_from_app_settings(settings, environment={})
    assert values == {
        "OPENAI_API_KEY": "saved-openai",
        "PEXELS_API_KEY": "saved-pexels",
        "PIXABAY_API_KEY": "saved-pixabay",
    }


def test_environment_is_fallback_for_blank_saved_credentials():
    settings = AppSettings()
    values = credentials_from_app_settings(settings, environment={
        "OPENAI_API_KEY": "env-openai",
        "PEXELS_API_KEY": "env-pexels",
        "PIXABAY_API_KEY": "env-pixabay",
    })
    assert values["OPENAI_API_KEY"] == "env-openai"
    assert values["PEXELS_API_KEY"] == "env-pexels"
    assert values["PIXABAY_API_KEY"] == "env-pixabay"


def test_saved_credentials_take_priority_over_environment():
    settings = AppSettings({
        ("ai", "api_key"): "saved-openai",
        ("images", "pexels_api_key"): "saved-pexels",
    })
    values = credentials_from_app_settings(settings, environment={
        "OPENAI_API_KEY": "env-openai",
        "PEXELS_API_KEY": "env-pexels",
        "PIXABAY_API_KEY": "env-pixabay",
    })
    assert values["OPENAI_API_KEY"] == "saved-openai"
    assert values["PEXELS_API_KEY"] == "saved-pexels"
    assert values["PIXABAY_API_KEY"] == "env-pixabay"


def test_credential_statuses_only_include_selected_services():
    statuses = credential_statuses(
        ProviderSettings(asset_providers=("pexels",)), credentials=credentials()
    )
    assert [status.name for status in statuses] == ["openai", "pexels"]
    assert all(status.configured for status in statuses)


def test_credential_statuses_show_missing_selected_service():
    statuses = credential_statuses(
        ProviderSettings(asset_providers=("pixabay",)),
        credentials=credentials(PIXABAY_API_KEY=""),
    )
    pixabay = next(status for status in statuses if status.name == "pixabay")
    assert not pixabay.configured
    assert pixabay.source == "PIXABAY_API_KEY"


def test_imported_scene_searches_preserve_repeated_scene_positions():
    project = {
        "notes": """0-5 sec
Search:
Yellowstone geothermal caldera landscape
Free Sources:
Pexels

5-10 sec
Search:
Yellowstone geothermal caldera landscape
Free Sources:
Pexels

10-15 sec
Search:
Mauna Loa Hawaii shield volcano
Free Sources:
Pixabay
"""
    }
    assert _imported_scene_searches(project) == [
        "Yellowstone geothermal caldera landscape",
        "Yellowstone geothermal caldera landscape",
        "Mauna Loa Hawaii shield volcano",
    ]


def fake_text_response(request):
    return {"output_text": "first query\nsecond query"}


def fake_asset_response(request):
    url = request.full_url
    if "pexels" in url:
        return {
            "photos": [
                {
                    "id": 1,
                    "width": 1080,
                    "height": 1920,
                    "alt": "first query second query vertical image",
                    "photographer": "A",
                    "src": {"portrait": "https://cdn.example/image-1.jpg"},
                },
                {
                    "id": 2,
                    "width": 1080,
                    "height": 1920,
                    "alt": "first query second query vertical image",
                    "photographer": "B",
                    "src": {"portrait": "https://cdn.example/image-2.jpg"},
                },
            ]
        }
    return {"hits": []}


def fake_downloader(url, destination):
    destination.write_bytes(b"asset")


def test_build_configured_providers_registers_real_pipeline_stages(tmp_path):
    configured = build_configured_providers(
        tmp_path,
        ProviderSettings(asset_providers=("pexels",), voice_provider="none"),
        credentials=credentials(),
        text_transport=fake_text_response,
        pexels_transport=fake_asset_response,
        downloader=fake_downloader,
    )
    assert configured.registry.get("research") is not None
    assert configured.registry.get("facts") is not None
    assert configured.registry.get("script") is not None
    assert configured.registry.get("image_prompts") is not None
    assert configured.registry.get("voice") is None


def test_image_prompt_stage_generates_queries_and_downloads_assets(tmp_path):
    configured = build_configured_providers(
        tmp_path,
        ProviderSettings(asset_providers=("pexels",), voice_provider="none"),
        credentials=credentials(),
        text_transport=fake_text_response,
        pexels_transport=fake_asset_response,
        downloader=fake_downloader,
    )
    context = SimpleNamespace(
        script="A script",
        image_prompts=None,
        timeline=None,
        project_folder=tmp_path,
    )
    results = configured.registry.require("image_prompts")(context)
    assert context.image_prompts == ["first query", "second query"]
    assert len(results) == 2
    assert all(result.path.is_file() for result in results)
    assert len({result.candidate.id for result in results}) == 2


def test_imported_scene_searches_are_not_polluted_by_overall_topic(tmp_path):
    configured = build_configured_providers(
        tmp_path,
        ProviderSettings(asset_providers=("pexels",), voice_provider="none"),
        credentials=credentials(),
        text_transport=fake_text_response,
        pexels_transport=fake_asset_response,
        downloader=fake_downloader,
    )
    context = SimpleNamespace(
        topic="Yellowstone Isn't the Biggest Volcano",
        script="A later scene about a broad shield volcano.",
        image_prompts=None,
        timeline=None,
        project_folder=tmp_path,
        project={
            "category": "Nature",
            "notes": "Search:\nMauna Loa Hawaii broad shield volcano\nFree Sources:\nPexels\n",
        },
        warnings=[],
    )

    configured.registry.require("image_prompts")(context)

    assert context.image_prompts == ["Mauna Loa Hawaii broad shield volcano"]
    assert not context.image_prompts[0].startswith("Nature ")
    assert "Yellowstone" not in context.image_prompts[0]


def test_build_requires_key_for_selected_asset_provider(tmp_path):
    with pytest.raises(ProviderSetupError, match="PEXELS_API_KEY"):
        build_configured_providers(
            tmp_path,
            ProviderSettings(asset_providers=("pexels",)),
            credentials=credentials(PEXELS_API_KEY=""),
            text_transport=fake_text_response,
        )


def test_build_requires_at_least_one_asset_provider(tmp_path):
    with pytest.raises(ProviderSetupError, match="at least one asset provider"):
        build_configured_providers(
            tmp_path,
            ProviderSettings(asset_providers=(), voice_provider="none"),
            credentials=credentials(),
            text_transport=fake_text_response,
        )
