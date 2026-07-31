from pathlib import Path

import pytest

from timeline import ProjectTimelineStore, SceneBuilder, TrackKind, build_project_timeline


def test_split_script_uses_blank_lines_and_normalizes_whitespace():
    builder = SceneBuilder()

    scenes = builder.split_script("First line.\r\n\r\n  Second   line.\ncontinued.  ")

    assert scenes == ["First line.", "Second line. continued."]


def test_blank_script_creates_empty_timeline_with_production_tracks():
    timeline = SceneBuilder().build("   ", name="Empty")

    assert timeline.name == "Empty"
    assert timeline.scenes == []
    assert [track.kind for track in timeline.tracks] == [
        TrackKind.VIDEO,
        TrackKind.AUDIO,
        TrackKind.SUBTITLE,
        TrackKind.MARKER,
    ]
    assert timeline.duration == 0


def test_builder_creates_one_scene_per_paragraph():
    timeline = SceneBuilder().build("Opening hook.\n\nSupporting fact.\n\nFinal payoff.")

    assert [scene.title for scene in timeline.scenes] == ["Scene 1", "Scene 2", "Scene 3"]
    assert [scene.narration for scene in timeline.scenes] == [
        "Opening hook.",
        "Supporting fact.",
        "Final payoff.",
    ]


def test_scene_timing_is_sequential_and_uses_speaking_rate():
    builder = SceneBuilder(words_per_minute=120, minimum_scene_duration=0.5)
    timeline = builder.build("one two three four\n\nfive six")

    assert timeline.scenes[0].start == 0
    assert timeline.scenes[0].duration == 2
    assert timeline.scenes[1].start == 2
    assert timeline.scenes[1].duration == 1
    assert timeline.duration == 3


def test_minimum_scene_duration_applies_to_short_paragraphs():
    builder = SceneBuilder(words_per_minute=300, minimum_scene_duration=1.25)

    assert builder.estimate_duration("Brief.") == 1.25


def test_scene_metadata_contains_editing_placeholders():
    scene = SceneBuilder().build("Octopuses have three hearts.").scenes[0]

    assert scene.metadata == {
        "scene_number": 1,
        "word_count": 4,
        "visuals": [],
        "keywords": [],
        "subtitle_text": "Octopuses have three hearts.",
        "transition": "cut",
        "notes": "",
    }


def test_builder_rejects_invalid_configuration_and_input():
    with pytest.raises(ValueError, match="words_per_minute"):
        SceneBuilder(words_per_minute=0)
    with pytest.raises(ValueError, match="minimum_scene_duration"):
        SceneBuilder(minimum_scene_duration=0)
    with pytest.raises(TypeError, match="script must be a string"):
        SceneBuilder().build(None)


def test_build_and_save_round_trips_through_project_store(tmp_path: Path):
    original = SceneBuilder().build_and_save(
        tmp_path,
        "Hook.\n\nExplanation goes here.",
        name="Saved fact",
    )

    restored = ProjectTimelineStore(tmp_path).load()

    assert restored == original
    assert (tmp_path / "timeline.json").is_file()


def test_build_project_timeline_convenience_api(tmp_path: Path):
    timeline = build_project_timeline(
        tmp_path,
        "One two three.",
        name="Convenience",
        words_per_minute=180,
        minimum_scene_duration=0.5,
    )

    assert timeline.name == "Convenience"
    assert timeline.scenes[0].duration == 1
    assert ProjectTimelineStore(tmp_path).load() == timeline
