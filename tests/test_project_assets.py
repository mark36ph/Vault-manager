from pathlib import Path

import pytest

from widgets.project_assets_panel import (
    ProjectAsset,
    delete_project_asset,
    rename_project_asset,
    scan_project_assets,
)


def test_scan_project_assets_finds_images_and_videos(tmp_path):
    images = tmp_path / "Assets" / "Images"
    videos = tmp_path / "Assets" / "Videos"
    images.mkdir(parents=True)
    videos.mkdir(parents=True)

    image = images / "saturn.jpg"
    video = videos / "rings.mp4"
    source = images / "saturn.source.txt"
    image.write_bytes(b"image")
    video.write_bytes(b"video")
    source.write_text("Provider: Pixabay\n", encoding="utf-8")
    (images / "ignore.txt").write_text("ignore", encoding="utf-8")

    assets = scan_project_assets(tmp_path)

    assert [asset.name for asset in assets] == ["saturn.jpg", "rings.mp4"]
    assert assets[0].media_type == "Image"
    assert assets[0].source_path == source
    assert assets[1].media_type == "Video"


def test_rename_project_asset_renames_source_file(tmp_path):
    image = tmp_path / "old.jpg"
    source = tmp_path / "old.source.txt"
    image.write_bytes(b"image")
    source.write_text("source", encoding="utf-8")
    asset = ProjectAsset(image, "Image", source)

    renamed = rename_project_asset(asset, "new-name")

    assert renamed.path == tmp_path / "new-name.jpg"
    assert renamed.path.exists()
    assert renamed.source_path == tmp_path / "new-name.source.txt"
    assert renamed.source_path.exists()
    assert not image.exists()
    assert not source.exists()


def test_rename_project_asset_rejects_changed_extension(tmp_path):
    image = tmp_path / "old.jpg"
    image.write_bytes(b"image")
    asset = ProjectAsset(image, "Image")

    with pytest.raises(ValueError, match="extension must remain"):
        rename_project_asset(asset, "new.png")


def test_rename_project_asset_rejects_existing_name(tmp_path):
    image = tmp_path / "old.jpg"
    existing = tmp_path / "new.jpg"
    image.write_bytes(b"image")
    existing.write_bytes(b"existing")
    asset = ProjectAsset(image, "Image")

    with pytest.raises(FileExistsError, match="already exists"):
        rename_project_asset(asset, "new")


def test_delete_project_asset_deletes_media_and_source(tmp_path):
    video = tmp_path / "clip.mp4"
    source = tmp_path / "clip.source.txt"
    video.write_bytes(b"video")
    source.write_text("source", encoding="utf-8")
    asset = ProjectAsset(video, "Video", source)

    delete_project_asset(asset)

    assert not video.exists()
    assert not source.exists()
