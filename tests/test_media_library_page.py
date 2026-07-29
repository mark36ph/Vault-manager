from pathlib import Path

from pages.media_library_page import (
    LibraryAsset,
    copy_library_asset_to_project,
    scan_media_library,
)


def test_scan_media_library_finds_images_and_videos_and_migrates_metadata(tmp_path):
    images = tmp_path / "Images"
    videos = tmp_path / "Videos"
    images.mkdir()
    videos.mkdir()
    image = images / "mars.jpg"
    video = videos / "launch.mp4"
    image.write_bytes(b"image")
    video.write_bytes(b"video")
    old_source = image.with_suffix(".source.txt")
    old_source.write_text("Provider: Pixabay\n", encoding="utf-8")

    assets = scan_media_library(tmp_path)

    expected_source = tmp_path / "Metadata" / "Images" / "mars.source.txt"
    assert [asset.name for asset in assets] == ["mars.jpg", "launch.mp4"]
    assert assets[0].source_path == expected_source
    assert expected_source.read_text(encoding="utf-8") == "Provider: Pixabay\n"
    assert not old_source.exists()
    assert assets[1].source_path is None


def test_copy_library_asset_to_project_copies_media_and_metadata(tmp_path):
    library = tmp_path / "library"
    project = tmp_path / "project"
    library.mkdir()
    media = library / "moon.jpg"
    source = tmp_path / "Metadata" / "Images" / "moon.source.txt"
    source.parent.mkdir(parents=True)
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
