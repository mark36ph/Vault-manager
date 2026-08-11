from pathlib import Path

from common.asset_acquisition import (
    AssetCandidate,
    _cached_asset_is_usable,
    _download_headers,
)


def test_download_headers_do_not_request_avif():
    accept = _download_headers("https://images.pexels.com/photos/1/example.jpeg")["Accept"]
    assert "image/avif" not in accept
    assert "image/jpeg" in accept
    assert "image/webp" in accept


def test_cached_avif_image_is_not_reused(tmp_path: Path):
    path = tmp_path / "photo.jpeg"
    path.write_bytes(b"\x00\x00\x00\x18ftypavif" + b"x" * 32)
    candidate = AssetCandidate(
        provider="pexels",
        id="1",
        url="https://example.test/photo.jpeg",
        kind="image",
    )
    assert _cached_asset_is_usable(candidate, path) is False


def test_cached_jpeg_image_is_reusable(tmp_path: Path):
    path = tmp_path / "photo.jpeg"
    path.write_bytes(b"\xff\xd8\xff" + b"x" * 32)
    candidate = AssetCandidate(
        provider="pexels",
        id="1",
        url="https://example.test/photo.jpeg",
        kind="image",
    )
    assert _cached_asset_is_usable(candidate, path) is True
