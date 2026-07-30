from pathlib import Path

import pytest

from timeline import (
    Clip,
    ClipKind,
    Scene,
    Timeline,
    TimelineBuilder,
    TimelineValidationError,
    TimelineValidator,
    Track,
    TrackKind,
    Transition,
)


def test_timeline_builder_creates_reusable_tracks_and_sorted_clips():
    builder = TimelineBuilder("Fact video", frame_rate=24)
    later = builder.add_clip(
        track_name="Video 1",
        track_kind=TrackKind.VIDEO,
        clip_kind=ClipKind.IMAGE,
        start=5,
        duration=2,
        source="images/later.png",
    )
    earlier = builder.add_clip(
        track_name="Video 1",
        track_kind=TrackKind.VIDEO,
        clip_kind=ClipKind.VIDEO,
        start=0,
        duration=5,
        source="video/opening.mp4",
    )

    timeline = builder.build()

    assert timeline.frame_rate == 24
    assert len(timeline.tracks) == 1
    assert timeline.tracks[0].clips == [earlier, later]
    assert timeline.duration == 7


def test_builder_rejects_reusing_track_name_with_different_kind():
    builder = TimelineBuilder("Fact video")
    builder.track("Primary", TrackKind.VIDEO)

    with pytest.raises(ValueError, match="already exists"):
        builder.track("Primary", TrackKind.AUDIO)


def test_timeline_round_trip_preserves_nested_models():
    transition = Transition(name="cross_dissolve", duration=0.5)
    clip = Clip(
        kind=ClipKind.IMAGE,
        start=0,
        duration=4,
        source="image.png",
        transition_out=transition,
        metadata={"zoom": 1.2},
    )
    track = Track(kind=TrackKind.VIDEO, name="Video 1", clips=[clip])
    scene = Scene(title="Opening", start=0, duration=4, clip_ids=[clip.id])
    original = Timeline(name="Fact video", tracks=[track], scenes=[scene])

    restored = Timeline.from_dict(original.to_dict())

    assert restored == original
    assert restored.tracks[0].clips[0].transition_out == transition
    assert restored.duration == 4


def test_model_rejects_invalid_time_values():
    with pytest.raises(ValueError, match="greater than zero"):
        Clip(kind=ClipKind.IMAGE, start=0, duration=0, source="image.png")

    with pytest.raises(ValueError, match="negative"):
        Transition(duration=-0.1)

    with pytest.raises(ValueError, match="frame_rate"):
        Timeline(name="Invalid", frame_rate=0)


def test_validator_reports_overlap_missing_source_and_unknown_scene_clip():
    track = Track(
        kind=TrackKind.VIDEO,
        name="Video 1",
        clips=[
            Clip(kind=ClipKind.IMAGE, start=0, duration=3, source="one.png"),
            Clip(kind=ClipKind.IMAGE, start=2, duration=3),
        ],
    )
    timeline = Timeline(
        name="Invalid",
        tracks=[track],
        scenes=[Scene(title="Opening", start=0, duration=5, clip_ids=["missing-id"])],
    )

    issues = TimelineValidator().validate(timeline)

    assert {issue.code for issue in issues} == {
        "overlapping_clips",
        "missing_source",
        "unknown_scene_clip",
    }


def test_validator_checks_media_files(tmp_path: Path):
    existing = tmp_path / "audio" / "voice.wav"
    existing.parent.mkdir()
    existing.write_bytes(b"audio")
    timeline = Timeline(
        name="Media",
        tracks=[
            Track(
                kind=TrackKind.AUDIO,
                name="Voiceover",
                clips=[
                    Clip(kind=ClipKind.AUDIO, start=0, duration=1, source="audio/voice.wav"),
                    Clip(kind=ClipKind.AUDIO, start=1, duration=1, source="audio/missing.wav"),
                ],
            )
        ],
    )

    issues = TimelineValidator().validate(timeline, media_root=tmp_path)

    assert [issue.code for issue in issues] == ["source_not_found"]


def test_validator_can_raise_structured_error():
    timeline = Timeline(
        name="Invalid",
        tracks=[
            Track(
                kind=TrackKind.VIDEO,
                name="Video 1",
                clips=[Clip(kind=ClipKind.IMAGE, start=0, duration=1)],
            )
        ],
    )

    with pytest.raises(TimelineValidationError) as exc_info:
        TimelineValidator().validate(timeline, raise_on_error=True)

    assert exc_info.value.issues[0].code == "missing_source"
