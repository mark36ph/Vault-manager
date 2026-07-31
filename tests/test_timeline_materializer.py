import pytest

from timeline import (
    Asset,
    AssetAssignmentEngine,
    Clip,
    ClipKind,
    ClipMaterializationError,
    SceneBuilder,
    TimelineClipMaterializer,
    Track,
    TrackKind,
    materialize_timeline_clips,
)


def make_timeline():
    return SceneBuilder().build("First scene.\n\nSecond scene.")


def assign(timeline, scene_index, kind, path, **options):
    return AssetAssignmentEngine(timeline).assign(
        timeline.scenes[scene_index].id,
        Asset(kind=kind, path=path, **options),
    )


def test_materializes_image_on_video_track():
    timeline = make_timeline()
    scene = timeline.scenes[0]
    asset = assign(timeline, 0, "image", "Assets/Images/earth.jpg")

    clips = materialize_timeline_clips(timeline)

    assert len(clips) == 1
    clip = clips[0]
    assert clip.kind is ClipKind.IMAGE
    assert clip.start == scene.start
    assert clip.duration == scene.duration
    assert clip.source == "Assets/Images/earth.jpg"
    assert clip.metadata["asset_id"] == asset.id
    assert timeline.get_track("Video 1").clips == [clip]
    assert scene.clip_ids == [clip.id]


def test_materializes_all_supported_asset_kinds():
    timeline = make_timeline()
    assign(timeline, 0, "video", "Assets/Videos/space.mp4")
    assign(timeline, 0, "audio", "Audio/narration.wav")
    assign(timeline, 0, "subtitle", "Subtitles/captions.srt")

    clips = TimelineClipMaterializer(timeline).materialize()

    assert [clip.kind for clip in clips] == [
        ClipKind.VIDEO,
        ClipKind.AUDIO,
        ClipKind.SUBTITLE,
    ]
    assert len(timeline.get_track("Video 1").clips) == 1
    assert len(timeline.get_track("Narration").clips) == 1
    assert len(timeline.get_track("Subtitles").clips) == 1


def test_materialization_is_idempotent_and_clip_ids_are_stable():
    timeline = make_timeline()
    assign(timeline, 0, "image", "image.jpg")
    materializer = TimelineClipMaterializer(timeline)

    first = materializer.materialize()
    second = materializer.materialize()

    assert [clip.id for clip in second] == [clip.id for clip in first]
    assert len(timeline.get_track("Video 1").clips) == 1
    assert timeline.scenes[0].clip_ids == [first[0].id]


def test_rebuild_preserves_manual_clips_and_scene_links():
    timeline = make_timeline()
    scene = timeline.scenes[0]
    manual = Clip(kind="marker", start=0, duration=1, name="Manual")
    marker_track = timeline.get_track("Markers")
    marker_track.add_clip(manual)
    scene.clip_ids.append(manual.id)
    assign(timeline, 0, "image", "image.jpg")

    TimelineClipMaterializer(timeline).materialize()
    TimelineClipMaterializer(timeline).materialize()

    assert marker_track.clips == [manual]
    assert manual.id in scene.clip_ids
    assert len(scene.clip_ids) == 2


def test_missing_standard_track_is_created():
    timeline = make_timeline()
    timeline.tracks = [track for track in timeline.tracks if track.name != "Narration"]
    assign(timeline, 0, "audio", "voice.wav")

    TimelineClipMaterializer(timeline).materialize()

    narration = timeline.get_track("Narration")
    assert narration is not None
    assert narration.kind is TrackKind.AUDIO
    assert len(narration.clips) == 1


def test_wrong_standard_track_kind_fails():
    timeline = make_timeline()
    timeline.tracks = [track for track in timeline.tracks if track.name != "Video 1"]
    timeline.add_track(Track(name="Video 1", kind=TrackKind.AUDIO))
    assign(timeline, 0, "image", "image.jpg")

    with pytest.raises(ClipMaterializationError, match="expected video"):
        TimelineClipMaterializer(timeline).materialize()


def test_assigned_asset_without_path_fails_before_mutating_tracks():
    timeline = make_timeline()
    AssetAssignmentEngine(timeline).assign(
        timeline.scenes[0].id,
        Asset(kind="image"),
    )

    with pytest.raises(ClipMaterializationError, match="has no path"):
        TimelineClipMaterializer(timeline).materialize()

    assert timeline.get_track("Video 1").clips == []


def test_pending_legacy_asset_is_not_materialized():
    timeline = make_timeline()
    timeline.scenes[0].metadata["assets"] = [
        Asset(kind="image", path="pending.jpg").to_dict()
    ]

    assert TimelineClipMaterializer(timeline).materialize() == []
    assert timeline.get_track("Video 1").clips == []


def test_scene_transition_is_applied_to_generated_clip():
    timeline = make_timeline()
    timeline.scenes[0].metadata["transition"] = "cross dissolve"
    assign(timeline, 0, "image", "image.jpg")

    clip = TimelineClipMaterializer(timeline).materialize()[0]

    assert clip.transition_in is not None
    assert clip.transition_in.name == "cross dissolve"


def test_asset_metadata_is_copied_to_clip():
    timeline = make_timeline()
    assign(
        timeline,
        0,
        "video",
        "clip.mp4",
        duration=12.5,
        source="Pexels",
        credit="Creator",
        license="Pexels license",
        metadata={"provider_id": "123"},
    )

    clip = TimelineClipMaterializer(timeline).materialize()[0]

    assert clip.metadata["asset_duration"] == 12.5
    assert clip.metadata["source"] == "Pexels"
    assert clip.metadata["credit"] == "Creator"
    assert clip.metadata["license"] == "Pexels license"
    assert clip.metadata["provider_id"] == "123"


def test_materializer_rejects_non_timeline_input():
    with pytest.raises(TypeError, match="timeline"):
        TimelineClipMaterializer(object())
