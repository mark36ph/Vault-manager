import json
import urllib.parse
from pathlib import Path
from types import SimpleNamespace

import pytest

from common.provider_integrations import (
    OpenAISpeechProvider,
    OpenAITextProvider,
    PexelsAssetProvider,
    PixabayAssetProvider,
    ProviderEnvironment,
    ProviderIntegrationError,
)


def request_json(request):
    return json.loads(request.data.decode("utf-8")) if request.data else None


def test_pexels_requires_key():
    with pytest.raises(ValueError, match="Pexels"):
        PexelsAssetProvider("")


def test_pexels_photo_search_maps_candidate_and_authorization():
    seen = {}

    def transport(request):
        seen["request"] = request
        return {"photos": [{"id": 7, "width": 2000, "height": 3000, "url": "https://pexels.test/photo", "photographer": "Ada", "alt": "Ocean", "src": {"portrait": "https://cdn.test/ocean.jpg"}}]}

    result = PexelsAssetProvider("secret", transport=transport).search("ocean", kind="image", limit=10)
    assert result[0].provider == "pexels"
    assert result[0].credit == "Ada"
    assert result[0].url.endswith("ocean.jpg")
    assert seen["request"].headers["Authorization"] == "secret"
    assert "orientation=portrait" in seen["request"].full_url


def test_pexels_video_search_selects_largest_file():
    payload = {"videos": [{"id": 3, "duration": 5, "video_files": [{"link": "small.mp4", "width": 640, "height": 360}, {"link": "large.mp4", "width": 1920, "height": 1080}], "user": {"name": "Sam"}}]}
    result = PexelsAssetProvider("key", transport=lambda request: payload).search("space", kind="video", limit=5)
    assert result[0].url == "large.mp4"
    assert result[0].duration == 5
    assert result[0].credit == "Sam"


def test_pexels_rejects_unknown_kind_without_request():
    provider = PexelsAssetProvider("key", transport=lambda request: (_ for _ in ()).throw(AssertionError()))
    assert provider.search("x", kind="audio", limit=2) == []


def test_pixabay_image_search_maps_fields_and_key():
    seen = {}

    def transport(request):
        seen["query"] = urllib.parse.parse_qs(urllib.parse.urlparse(request.full_url).query)
        return {"hits": [{"id": 9, "largeImageURL": "https://cdn.test/forest.jpg", "imageWidth": 1200, "imageHeight": 2000, "likes": 4, "downloads": 2000, "tags": "forest, trees", "user": "Lin", "pageURL": "https://pixabay.test/9"}]}

    result = PixabayAssetProvider("px", transport=transport).search("forest", kind="image", limit=4)
    assert result[0].score == 6
    assert result[0].license == "Pixabay Content License"
    assert seen["query"]["key"] == ["px"]
    assert seen["query"]["orientation"] == ["vertical"]


def test_pixabay_video_search_selects_largest_version():
    payload = {"hits": [{"id": 2, "videos": {"tiny": {"url": "tiny.mp4", "width": 640, "height": 360}, "medium": {"url": "medium.mp4", "width": 1920, "height": 1080}}, "duration": 8}]}
    result = PixabayAssetProvider("key", transport=lambda request: payload).search("clouds", kind="video", limit=3)
    assert result[0].url == "medium.mp4"
    assert result[0].width == 1920


def test_pixabay_limits_query_to_one_hundred_characters():
    seen = {}
    provider = PixabayAssetProvider("key", transport=lambda request: seen.setdefault("url", request.full_url) and {"hits": []})
    provider.search("x" * 150, kind="image", limit=3)
    query = urllib.parse.parse_qs(urllib.parse.urlparse(seen["url"]).query)["q"][0]
    assert len(query) == 100


def test_openai_text_provider_posts_responses_payload():
    seen = {}

    def transport(request):
        seen["request"] = request
        return {"output_text": "A polished script."}

    provider = OpenAITextProvider("key", instructions="Write clearly", prompt_builder=lambda context: context.topic, model="test-model", transport=transport)
    result = provider(SimpleNamespace(topic="Ocean facts"))
    assert result == "A polished script."
    body = request_json(seen["request"])
    assert body == {"model": "test-model", "instructions": "Write clearly", "input": "Ocean facts"}
    assert seen["request"].headers["Authorization"] == "Bearer key"


def test_openai_text_provider_reads_nested_output():
    payload = {"output": [{"content": [{"type": "output_text", "text": "Nested text"}]}]}
    provider = OpenAITextProvider("key", instructions="x", prompt_builder=lambda context: "prompt", transport=lambda request: payload)
    assert provider(object()) == "Nested text"


def test_openai_text_provider_rejects_empty_response():
    provider = OpenAITextProvider("key", instructions="x", prompt_builder=lambda context: "prompt", transport=lambda request: {})
    with pytest.raises(ProviderIntegrationError, match="contain text"):
        provider(object())


def test_openai_text_provider_rejects_empty_prompt():
    provider = OpenAITextProvider("key", instructions="x", prompt_builder=lambda context: "", transport=lambda request: {})
    with pytest.raises(ValueError, match="prompt"):
        provider(object())


def test_openai_speech_writes_audio_atomically(tmp_path):
    seen = {}

    def transport(request):
        seen["body"] = request_json(request)
        return b"audio bytes"

    context = SimpleNamespace(script="Narration text", project_folder=tmp_path)
    result = OpenAISpeechProvider("key", voice="nova", response_format="mp3", transport=transport)(context)
    path = Path(result)
    assert path.read_bytes() == b"audio bytes"
    assert path.name == "narration.mp3"
    assert seen["body"]["voice"] == "nova"
    assert not path.with_suffix(".mp3.part").exists()


def test_openai_speech_rejects_empty_audio(tmp_path):
    provider = OpenAISpeechProvider("key", transport=lambda request: b"")
    with pytest.raises(ProviderIntegrationError, match="empty"):
        provider(SimpleNamespace(script="hello", project_folder=tmp_path))


def test_openai_speech_requires_script(tmp_path):
    provider = OpenAISpeechProvider("key", transport=lambda request: b"x")
    with pytest.raises(ValueError, match="script"):
        provider(SimpleNamespace(script="", project_folder=tmp_path))


def test_provider_environment_reads_expected_variables():
    environment = ProviderEnvironment.from_env({"OPENAI_API_KEY": "o", "PEXELS_API_KEY": "p", "PIXABAY_API_KEY": "x"})
    assert environment.openai_api_key == "o"
    assert environment.pexels_api_key == "p"
    assert environment.pixabay_api_key == "x"


def test_provider_environment_defaults_to_empty_values():
    assert ProviderEnvironment.from_env({}) == ProviderEnvironment()
