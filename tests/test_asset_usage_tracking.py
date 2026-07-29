from widgets.asset_usage_tracking import find_asset_references
from widgets.project_assets_panel import ProjectAsset


def test_find_asset_references_finds_filename_and_relative_path(tmp_path):
    images = tmp_path / "Assets" / "Images"
    images.mkdir(parents=True)
    image = images / "saturn.jpg"
    source = images / "saturn.source.txt"
    image.write_bytes(b"image")
    source.write_text("Source page: example\n", encoding="utf-8")

    notes = tmp_path / "notes.md"
    script = tmp_path / "script.txt"
    notes.write_text("Use saturn.jpg in the opening shot.\n", encoding="utf-8")
    script.write_text("Asset: Assets/Images/saturn.jpg\n", encoding="utf-8")

    asset = ProjectAsset(image, "Image", source)

    references = find_asset_references(asset, tmp_path)

    assert references == [notes, script]


def test_find_asset_references_ignores_source_metadata_and_binary_files(tmp_path):
    videos = tmp_path / "Assets" / "Videos"
    videos.mkdir(parents=True)
    video = videos / "rings.mp4"
    source = videos / "rings.source.txt"
    video.write_bytes(b"video")
    source.write_text("Original filename: rings.mp4\n", encoding="utf-8")
    (tmp_path / "archive.bin").write_bytes(b"rings.mp4")

    asset = ProjectAsset(video, "Video", source)

    assert find_asset_references(asset, tmp_path) == []


def test_find_asset_references_ignores_venv_files(tmp_path):
    images = tmp_path / "Assets" / "Images"
    images.mkdir(parents=True)
    image = images / "earth.png"
    image.write_bytes(b"image")

    venv_file = tmp_path / ".venv" / "notes.txt"
    venv_file.parent.mkdir()
    venv_file.write_text("earth.png", encoding="utf-8")

    asset = ProjectAsset(image, "Image")

    assert find_asset_references(asset, tmp_path) == []
