from pathlib import Path

import pytest

from common.resolve_timeline_adapter import (
    ResolveTimelineAdapter,
    ResolveTimelineAdapterError,
    build_resolve_timeline_plan,
)
from timeline import (
    Asset,
    AssetAssignmentEngine,
    Clip,
    ClipKind,
    SceneBuilder,
    TimelineClipMaterializer,
    Track,
    TrackKind,
    Transition,
)


def materialized_timeline(tmp_path: Path):
    image = tmp_path / "Assets" / "image.jpg"
    audio = tmp_path / "Voice" / "narration.wav"
    image.parent.mkdir(parents=True)
    audio.parent.mkdir(parents=True)
    image.write_bytes(b"image")
    audio.write_bytes(b"audio")

    timeline = SceneBuilder().build("Opening fact.", name="Resolve Fact")
    scene = timeline.scenes[0]
    engine = AssetAssignmentEngine(timeline)
    engine.assign(scene.id, Asset(kind="image", path="Assets/image.jpg"))
    engine.assign(scene.id, Asset(kind="audio", path="Voice/narration.wav"))
    TimelineClipMaterializer(timeline).materialize()
    return timeline


def test_adapter_rejects_non_timeline():
    with pytest.raises(TypeError, match="timeline"):
        ResolveTimelineAdapter({})


def test_plan_contains_timeline_settings_and_scenes(tmp_path):
    timeline = materialized_timeline(tmp_path)
    plan = build_resolve_timeline_plan(timeline, project_folder=tmp_path)

    assert plan["name"] == "Resolve Fact"
    assert plan["frame_rate"] == 30
    assert plan["resolution"] == [1920, 1080]
    assert plan["duration"] == timeline.duration
    assert plan["scenes"][0]["narration"] == "Opening fact."


def test_adapter_maps_video_and_audio_tracks(tmp_path):
    plan = build_resolve_timeline_plan(materialized_timeline(tmp_path), project_folder=tmp_path)
    tracks = {track["name"]: track for track in plan["tracks"]}

    assert tracks["Video 1"]["kind"] == "video"
    assert tracks["Video 1"]["clips"][0]["kind"] == "image"
    assert tracks["Narration"]["clips"][0]["kind"] == "audio"


def test_adapter_resolves_project_relative_sources(tmp_path):
    plan = build_resolve_timeline_plan(materialized_timeline(tmp_path), project_folder=tmp_path)
    video_clip = next(track for track in plan["tracks"] if track["name"] == "Video 1")["clips"][0]

    assert video_clip["source"] == str((tmp_path / "Assets" / "image.jpg").resolve())


def test_adapter_preserves_transition_and_metadata(tmp_path):
    timeline = materialized_timeline(tmp_path)
    clip = timeline.get_track("Video 1").clips[0]
    clip.transition_out = Transition(name="cross_dissolve", duration=0.25)
    clip.metadata["credit"] = "Example source"

    plan = build_resolve_timeline_plan(timeline, project_folder=tmp_path)
    exported = next(track for track in plan["tracks"] if track["name"] == "Video 1")["clips"][0]

    assert exported["transition_out"] == {"name": "cross_dissolve", "duration": 0.25}
    assert exported["metadata"]["credit"] == "Example source"


def test_strict_export_rejects_missing_source(tmp_path):
    timeline = materialized_timeline(tmp_path)
    (tmp_path / "Assets" / "image.jpg").unlink()

    with pytest.raises(ResolveTimelineAdapterError, match="does not exist"):
        build_resolve_timeline_plan(timeline, project_folder=tmp_path)


def test_non_strict_export_returns_warnings(tmp_path):
    timeline = materialized_timeline(tmp_path)
    (tmp_path / "Assets" / "image.jpg").unlink()

    plan = build_resolve_timeline_plan(timeline, project_folder=tmp_path, strict=False)

    assert any("does not exist" in warning for warning in plan["warnings"])


def test_adapter_rejects_clip_on_incompatible_track(tmp_path):
    timeline = materialized_timeline(tmp_path)
    timeline.add_track(
        Track(
            kind=TrackKind.AUDIO,
            name="Broken",
            clips=[Clip(kind=ClipKind.IMAGE, start=0, duration=1, source="Assets/image.jpg")],
        )
    )

    with pytest.raises(ResolveTimelineAdapterError, match="incompatible"):
        build_resolve_timeline_plan(timeline, project_folder=tmp_path)


def test_scene_clip_ids_match_exported_clips(tmp_path):
    timeline = materialized_timeline(tmp_path)
    plan = build_resolve_timeline_plan(timeline, project_folder=tmp_path)

    exported_ids = {
        clip["id"]
        for track in plan["tracks"]
        for clip in track["clips"]
    }
    assert set(plan["scenes"][0]["clip_ids"]).issubset(exported_ids)
