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
        "hits": [{
            "id": 123,
            "pageURL": "https://pixabay.com/photos/example-123/",
            "tags": "mars, planet, space",
            "webformatURL": "https://example.com/preview.jpg",
            "largeImageURL": "https://example.com/full.jpg",
            "imageWidth": 1920,
            "imageHeight": 1080,
            "user": "Photographer",
            "userImageURL": "https://example.com/user.jpg",
        }]
    }

    monkeypatch.setattr(
        image_search.urllib.request,
        "urlopen",
        lambda request, timeout: FakeResponse(json.dumps(payload).encode("utf-8")),
    )
    results = search_pixabay_images("Mars", "test-key", orientation="vertical")
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

    monkeypatch.setattr(image_search.urllib.request, "urlopen", fake_urlopen)
    with pytest.raises(ImageSearchError, match="offline"):
        search_pixabay_images("Mars", "test-key")


def _image_result(image_id=456, tags="red planet, mars"):
    return ImageSearchResult(
        image_id=image_id,
        preview_url="https://example.com/preview.jpg",
        download_url="https://example.com/full.jpg",
        page_url=f"https://pixabay.com/photos/example-{image_id}/",
        creator="Example Creator",
        creator_url="",
        tags=tags,
        width=1920,
        height=1080,
    )


def test_download_image_to_project_separates_library_metadata(monkeypatch, tmp_path):
    result = _image_result()
    library_root = tmp_path / "Library"
    project_root = tmp_path / "Project"
    monkeypatch.setattr(
        image_search.urllib.request,
        "urlopen",
        lambda request, timeout: FakeResponse(b"image-bytes"),
    )

    image_path = download_image_to_project(result, project_root, library_root=library_root)

    library_path = library_root / "Images" / "red-planet-456.jpg"
    library_source = library_root / "Metadata" / "Images" / "red-planet-456.source.txt"
    project_source = image_path.with_suffix(".source.txt")
    assert image_path.parent == project_root / "Assets" / "Images"
    assert image_path.read_bytes() == b"image-bytes"
    assert library_path.read_bytes() == b"image-bytes"
    assert library_source.exists()
    assert not library_path.with_suffix(".source.txt").exists()
    assert project_source.read_text(encoding="utf-8") == library_source.read_text(encoding="utf-8")
    assert "Provider: Pixabay" in library_source.read_text(encoding="utf-8")


def test_download_reuses_library_copy_without_downloading_again(monkeypatch, tmp_path):
    result = _image_result(image_id=789, tags="moon")
    library_root = tmp_path / "Library"
    download_count = 0

    def fake_urlopen(request, timeout):
        nonlocal download_count
        download_count += 1
        return FakeResponse(b"shared-image")

    monkeypatch.setattr(image_search.urllib.request, "urlopen", fake_urlopen)
    first = download_image_to_project(result, tmp_path / "Project One", library_root=library_root)
    second = download_image_to_project(result, tmp_path / "Project Two", library_root=library_root)

    assert download_count == 1
    assert first.read_bytes() == b"shared-image"
    assert second.read_bytes() == b"shared-image"
    assert len(list((library_root / "Images").glob("*.jpg"))) == 1


def test_download_image_avoids_duplicate_project_filenames(monkeypatch, tmp_path):
    result = _image_result(image_id=987, tags="moon")
    library_root = tmp_path / "Library"
    project_root = tmp_path / "Project"
    monkeypatch.setattr(
        image_search.urllib.request,
        "urlopen",
        lambda request, timeout: FakeResponse(b"image"),
    )

    first = download_image_to_project(result, project_root, library_root=library_root)
    second = download_image_to_project(result, project_root, library_root=library_root)

    assert first.name == "moon-987.jpg"
    assert second.name == "moon-987-2.jpg"
    assert len(list((library_root / "Images").glob("*.jpg"))) == 1
