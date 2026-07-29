from pathlib import Path

from pages.media_library_page import (
    LibraryAsset,
    copy_library_asset_to_project,
    scan_media_library,
)


def test_scan_media_library_finds_images_and_videos(tmp_path):
    images = tmp_path / "Images"
    videos = tmp_path / "Videos"
    images.mkdir()
    videos.mkdir()
    image = images / "mars.jpg"
    video = videos / "launch.mp4"
    image.write_bytes(b"image")
    video.write_bytes(b"video")
    image.with_suffix(".source.txt").write_text("Provider: Pixabay\n", encoding="utf-8")

    assets = scan_media_library(tmp_path)

    assert [asset.name for asset in assets] == ["mars.jpg", "launch.mp4"]
    assert assets[0].source_path == image.with_suffix(".source.txt")
    assert assets[1].source_path is None


def test_copy_library_asset_to_project_copies_media_and_metadata(tmp_path):
    library = tmp_path / "library"
    project = tmp_path / "project"
    library.mkdir()
    media = library / "moon.jpg"
    source = library / "moon.source.txt"
    media.write_bytes(b"image-data")
    source.write_text("Provider: Pexels\n", encoding="utf-8")
    asset = LibraryAsset(media, "Image", source)

    destination = copy_library_asset_to_project(asset, project)

    assert destination == project / "Assets" / "Images" / "moon.jpg"
    assert destination.read_bytes() == b"image-data"
    assert destination.with_suffix(".source.txt").read_text(encoding="utf-8") == "Provider: Pexels\n"


def test_copy_library_asset_to_project_avoids_overwriting(tmp_path):
    library = tmp_path / "library"
    project = tmp_path / "project"
    library.mkdir()
    media = library / "clip.mp4"
    media.write_bytes(b"new")
    existing_folder = project / "Assets" / "Videos"
    existing_folder.mkdir(parents=True)
    (existing_folder / "clip.mp4").write_bytes(b"existing")

    destination = copy_library_asset_to_project(
        LibraryAsset(media, "Video"),
        project,
    )

    assert destination.name == "clip-2.mp4"
    assert destination.read_bytes() == b"new"
    assert (existing_folder / "clip.mp4").read_bytes() == b"existing"
