import json

import pytest

from common.resolve_complete_export import export_complete_resolve_package
from timeline import Clip, ClipKind, Scene, Timeline, Track, TrackKind


def project_data():
    return {
        "title": "Complete Export",
        "script": "A short fact.",
        "subtitle_text": "A short fact.",
        "sources": "Example",
    }


def settings():
    return {
        "timeline_width": 1080,
        "timeline_height": 1920,
        "frame_rate": 30,
        "default_project_name": "Complete Export",
    }


def make_timeline(tmp_path):
    image = tmp_path / "Assets" / "image.jpg"
    image.parent.mkdir(parents=True)
    image.write_bytes(b"image")
    clip = Clip(
        id="clip-1",
        kind=ClipKind.IMAGE,
        start=0,
        duration=3,
        source="Assets/image.jpg",
        metadata={"scene_id": "scene-1"},
    )
    track = Track(id="track-1", name="Video 1", kind=TrackKind.VIDEO, clips=[clip])
    scene = Scene(
        id="scene-1",
        title="Scene 1",
        start=0,
        duration=3,
        narration="A short fact.",
        clip_ids=["clip-1"],
    )
    return Timeline(
        id="timeline-1",
        name="Complete Timeline",
        width=1080,
        height=1920,
        frame_rate=30,
        tracks=[track],
        scenes=[scene],
    )


def test_complete_export_requires_timeline(tmp_path):
    with pytest.raises(TypeError, match="timeline"):
        export_complete_resolve_package(project_data(), tmp_path, settings(), None)


def test_complete_export_writes_timeline_plan(tmp_path):
    result = export_complete_resolve_package(
        project_data(), tmp_path, settings(), make_timeline(tmp_path)
    )
    plan = json.loads(result.timeline_plan.read_text(encoding="utf-8"))
    assert plan["name"] == "Complete Timeline"
    assert plan["resolution"] == [1080, 1920]
    assert plan["tracks"][0]["clips"][0]["id"] == "clip-1"


def test_complete_export_resolves_media_paths(tmp_path):
    result = export_complete_resolve_package(
        project_data(), tmp_path, settings(), make_timeline(tmp_path)
    )
    plan = json.loads(result.timeline_plan.read_text(encoding="utf-8"))
    assert plan["tracks"][0]["clips"][0]["source"] == str(
        (tmp_path / "Assets" / "image.jpg").resolve()
    )


def test_complete_export_includes_legacy_and_complete_files(tmp_path):
    result = export_complete_resolve_package(
        project_data(), tmp_path, settings(), make_timeline(tmp_path)
    )
    names = {path.name for path in result.files}
    assert "media_manifest.json" in names
    assert "timeline_settings.json" in names
    assert "resolve_timeline_plan.json" in names
    assert "build_complete_resolve_timeline.py" in names
    assert "README_COMPLETE.txt" in names
    assert all(path.exists() for path in result.files)


def test_generated_runner_uses_resolve_builder(tmp_path):
    result = export_complete_resolve_package(
        project_data(), tmp_path, settings(), make_timeline(tmp_path)
    )
    script = (result.export_folder / "build_complete_resolve_timeline.py").read_text(
        encoding="utf-8"
    )
    assert "DaVinciResolveScript" in script
    assert "build_resolve_timeline" in script
    assert "resolve_timeline_plan.json" in script


def test_complete_export_non_strict_collects_adapter_warnings(tmp_path):
    timeline = make_timeline(tmp_path)
    timeline.tracks[0].clips[0].source = "Assets/missing.jpg"
    result = export_complete_resolve_package(
        project_data(), tmp_path, settings(), timeline, strict=False
    )
    assert any("does not exist" in warning for warning in result.warnings)
