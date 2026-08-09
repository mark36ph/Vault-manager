from types import SimpleNamespace

from common.provider_setup import (
    ProviderCredentials,
    ProviderSettings,
    build_configured_providers,
)


def credentials():
    return ProviderCredentials({
        "OPENAI_API_KEY": "openai-key",
        "PEXELS_API_KEY": "pexels-key",
        "PIXABAY_API_KEY": "pixabay-key",
    })


def fake_asset_response(request):
    if "pexels" not in request.full_url:
        return {"hits": []}
    return {
        "photos": [
            {
                "id": 1,
                "width": 1080,
                "height": 1920,
                "alt": "space planet vertical image",
                "photographer": "A",
                "src": {"portrait": "https://cdn.example/image-1.jpg"},
            },
            {
                "id": 2,
                "width": 1080,
                "height": 1920,
                "alt": "space planet rotation vertical image",
                "photographer": "B",
                "src": {"portrait": "https://cdn.example/image-2.jpg"},
            },
        ]
    }


def fake_downloader(url, destination):
    destination.write_bytes(b"asset")


def fail_if_text_prompt_is_used(_request):
    raise AssertionError("OpenAI image-query generation should be skipped when imported searches exist")


def test_imported_timeline_searches_are_preferred_and_category_anchored(tmp_path):
    configured = build_configured_providers(
        tmp_path,
        ProviderSettings(asset_providers=("pexels",), voice_provider="none"),
        credentials=credentials(),
        text_transport=fail_if_text_prompt_is_used,
        pexels_transport=fake_asset_response,
        downloader=fake_downloader,
    )
    context = SimpleNamespace(
        script="Venus rotates slowly. Venus orbits the Sun.",
        topic="Venus Takes Longer to Rotate Than to Orbit the Sun",
        project={
            "category": "Space",
            "notes": (
                "0–4 sec\nSearch:\nVenus planet space\nVenus solar system\n"
                "Free Sources:\nPexels\nPixabay\n\n"
                "4–9 sec\nSearch:\nrotating planet\nspace planet rotation\n"
                "Free Sources:\nPexels\nPixabay"
            ),
        },
        image_prompts=None,
        timeline=None,
        project_folder=tmp_path,
        warnings=[],
    )

    results = configured.registry.require("image_prompts")(context)

    assert context.image_prompts[:2] == [
        "Space Venus planet space",
        "Space rotating planet",
    ]
    assert any("imported scene search queries" in warning for warning in context.warnings)
    assert results
