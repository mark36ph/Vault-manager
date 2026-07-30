import json
from pathlib import Path

from common.resolve_export import (
    build_scene_plan,
    collect_media_manifest,
    export_resolve_package,
    validate_resolve_export,
)


def resolve_settings():
    return {
        "timeline_width": 1080,
        "timeline_height": 1920,
        "frame_rate": 30,
        "default_project_name": "Fact Vault Video",
    }


def project_data():
    return {
        "title": "Titanic Facts",
        "script": "The Titanic carried more than 2,200 people.",
        "on_screen_text": "2,200+ people",
        "visual_plan": "Show the Titanic at sea.",
        "subtitle_text": "The Titanic carried more than 2,200 people.",
        "sources": "Example source",
        "narration_duration": 4.2,
    }


def test_collect_media_manifest_groups_supported_files(tmp_path):
    images = tmp_path / "Assets" / "Images"
    videos = tmp_path / "Assets" / "Videos"
    voice = tmp_path / "Voice"
    images.mkdir(parents=True)
    videos.mkdir(parents=True)
    voice.mkdir(parents=True)

    (images / "ship.jpg").write_bytes(b"image")
    (videos / "ocean.mp4").write_bytes(b"video")
    (voice / "narration.wav").write_bytes(b"audio")
    (tmp_path / "notes.txt").write_text("ignore", encoding="utf-8")

    manifest = collect_media_manifest(tmp_path)

    assert [item["path"] for item in manifest["images"]] == ["Assets/Images/ship.jpg"]
    assert [item["path"] for item in manifest["videos"]] == ["Assets/Videos/ocean.mp4"]
    assert [item["path"] for item in manifest["audio"]] == ["Voice/narration.wav"]


def test_collect_media_manifest_does_not_include_resolve_output(tmp_path):
    resolve_folder = tmp_path / "Resolve"
    resolve_folder.mkdir()
    (resolve_folder / "preview.jpg").write_bytes(b"generated")

    manifest = collect_media_manifest(tmp_path)

    assert manifest["images"] == []


def test_build_scene_plan_uses_vertical_timeline_settings():
    plan = build_scene_plan(project_data(), resolve_settings())

    assert plan["project"] == "Titanic Facts"
    assert plan["resolution"] == [1080, 1920]
    assert plan["fps"] == 30
    assert plan["scenes"][0]["duration"] == 4.2
    assert plan["scenes"][0]["caption"] == "2,200+ people"


def test_validation_returns_actionable_warnings_for_empty_project():
    manifest = {"images": [], "videos": [], "audio": []}
    scene_plan = {"scenes": []}

    warnings = validate_resolve_export({"subtitle_text": ""}, manifest, scene_plan)

    assert "No image or video media was found in the project." in warnings
    assert "No narration or other audio file was found in the project." in warnings
    assert "No scene timing or visual plan is available yet." in warnings
    assert "No subtitle text is available." in warnings


def test_export_resolve_package_writes_expected_files(tmp_path):
    image_folder = tmp_path / "Assets" / "Images"
    voice_folder = tmp_path / "Voice"
    image_folder.mkdir(parents=True)
    voice_folder.mkdir(parents=True)
    (image_folder / "titanic.jpg").write_bytes(b"image")
    (voice_folder / "narration.wav").write_bytes(b"audio")

    result = export_resolve_package(project_data(), tmp_path, resolve_settings())

    expected = {
        "scene_plan.json",
        "media_manifest.json",
        "timeline_settings.json",
        "source_notes.json",
        "build_resolve_timeline.py",
        "README.txt",
    }
    assert {path.name for path in result.files} == expected
    assert result.export_folder == tmp_path / "Resolve"
    assert all(path.exists() for path in result.files)

    timeline = json.loads((result.export_folder / "timeline_settings.json").read_text(encoding="utf-8"))
    manifest = json.loads((result.export_folder / "media_manifest.json").read_text(encoding="utf-8"))

    assert timeline == {
        "project_name": "Fact Vault Video",
        "width": 1080,
        "height": 1920,
        "fps": 30,
    }
    assert manifest["images"][0]["path"] == "Assets/Images/titanic.jpg"
    assert manifest["audio"][0]["path"] == "Voice/narration.wav"
    assert result.warnings == ()


def test_export_resolve_package_rejects_missing_project_folder(tmp_path):
    missing = tmp_path / "missing"

    try:
        export_resolve_package(project_data(), missing, resolve_settings())
    except FileNotFoundError as error:
        assert str(missing) in str(error)
    else:
        raise AssertionError("Expected FileNotFoundError")
