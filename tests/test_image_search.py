import io
import json
import urllib.error

import pytest

import image_search
from image_search import (
    ImageSearchError,
    ImageSearchResult,
    download_image_to_project,
    search_pixabay_images,
)


class FakeResponse:
    def __init__(self, data):
        self.data = data

    def __enter__(self):
        return self

    def __exit__(self, exc_type, exc_value, traceback):
        return False

    def read(self):
        return self.data


def test_search_pixabay_images_returns_normalised_results(monkeypatch):
    payload = {
        "hits": [
            {
                "id": 123,
                "pageURL": "https://pixabay.com/photos/example-123/",
                "tags": "mars, planet, space",
                "webformatURL": "https://example.com/preview.jpg",
                "largeImageURL": "https://example.com/full.jpg",
                "imageWidth": 1920,
                "imageHeight": 1080,
                "user": "Photographer",
                "userImageURL": "https://example.com/user.jpg",
            }
        ]
    }

    def fake_urlopen(request, timeout):
        return FakeResponse(json.dumps(payload).encode("utf-8"))

    monkeypatch.setattr(
        image_search.urllib.request,
        "urlopen",
        fake_urlopen,
    )

    results = search_pixabay_images(
        "Mars",
        "test-key",
        orientation="vertical",
    )

    assert len(results) == 1
    assert results[0].image_id == 123
    assert results[0].creator == "Photographer"
    assert results[0].download_url == "https://example.com/full.jpg"
    assert results[0].tags == "mars, planet, space"


def test_search_pixabay_images_requires_query():
    with pytest.raises(ValueError, match="search term"):
        search_pixabay_images("", "test-key")


def test_search_pixabay_images_requires_api_key():
    with pytest.raises(ValueError, match="API key"):
        search_pixabay_images("Mars", "")


def test_search_pixabay_images_reports_network_error(monkeypatch):
    def fake_urlopen(request, timeout):
        raise urllib.error.URLError("offline")

    monkeypatch.setattr(
        image_search.urllib.request,
        "urlopen",
        fake_urlopen,
    )

    with pytest.raises(ImageSearchError, match="offline"):
        search_pixabay_images("Mars", "test-key")


def test_download_image_to_project_saves_image_and_source(monkeypatch, tmp_path):
    result = ImageSearchResult(
        image_id=456,
        preview_url="https://example.com/preview.jpg",
        download_url="https://example.com/full.jpg",
        page_url="https://pixabay.com/photos/example-456/",
        creator="Example Creator",
        creator_url="",
        tags="red planet, mars",
        width=1920,
        height=1080,
    )

    def fake_urlopen(request, timeout):
        return FakeResponse(b"image-bytes")

    monkeypatch.setattr(
        image_search.urllib.request,
        "urlopen",
        fake_urlopen,
    )

    image_path = download_image_to_project(result, tmp_path)

    assert image_path.parent == tmp_path / "Assets" / "Images"
    assert image_path.read_bytes() == b"image-bytes"

    source_path = image_path.with_suffix(".source.txt")
    source_text = source_path.read_text(encoding="utf-8")

    assert "Provider: Pixabay" in source_text
    assert "Creator: Example Creator" in source_text
    assert result.page_url in source_text


def test_download_image_avoids_duplicate_filenames(monkeypatch, tmp_path):
    result = ImageSearchResult(
        image_id=789,
        preview_url="https://example.com/preview.jpg",
        download_url="https://example.com/full.jpg",
        page_url="https://pixabay.com/photos/example-789/",
        creator="Creator",
        creator_url="",
        tags="moon",
        width=1000,
        height=1000,
    )

    def fake_urlopen(request, timeout):
        return FakeResponse(b"image")

    monkeypatch.setattr(
        image_search.urllib.request,
        "urlopen",
        fake_urlopen,
    )

    first = download_image_to_project(result, tmp_path)
    second = download_image_to_project(result, tmp_path)

    assert first.name == "moon-789.jpg"
    assert second.name == "moon-789-2.jpg"