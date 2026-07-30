import json
from pathlib import Path

import pytest

from timeline import (
    ClipKind,
    ProjectTimelineStore,
    TimelineBuilder,
    TimelineStorageError,
    TrackKind,
    ensure_project_timeline,
)


def test_create_writes_empty_timeline_json(tmp_path: Path):
    project = tmp_path / "Fact Project"
    store = ProjectTimelineStore(project)

    timeline = store.create("Fact Project", frame_rate=24, width=1080, height=1920)

    assert store.path == project / "timeline.json"
    assert store.path.is_file()
    payload = json.loads(store.path.read_text(encoding="utf-8"))
    assert payload["name"] == "Fact Project"
    assert payload["frame_rate"] == 24
    assert payload["width"] == 1080
    assert payload["height"] == 1920
    assert payload["tracks"] == []
    assert payload["scenes"] == []
    assert store.load() == timeline


def test_ensure_creates_timeline_for_legacy_project(tmp_path: Path):
    project = tmp_path / "Legacy"
    project.mkdir()
    (project / "Script.txt").write_text("Existing project", encoding="utf-8")

    timeline = ensure_project_timeline(project, "Legacy")

    assert timeline.name == "Legacy"
    assert (project / "timeline.json").is_file()


def test_ensure_preserves_existing_timeline(tmp_path: Path):
    store = ProjectTimelineStore(tmp_path)
    builder = TimelineBuilder("Original")
    builder.add_clip(
        track_name="Video 1",
        track_kind=TrackKind.VIDEO,
        clip_kind=ClipKind.IMAGE,
        start=0,
        duration=3,
        source="Assets/Images/fact.png",
    )
    original = builder.build()
    store.save(original)

    loaded = store.ensure("Replacement name")

    assert loaded == original
    assert loaded.name == "Original"
    assert len(loaded.tracks[0].clips) == 1


def test_save_and_load_round_trip_timeline_changes(tmp_path: Path):
    store = ProjectTimelineStore(tmp_path)
    builder = TimelineBuilder("Round trip")
    clip = builder.add_clip(
        track_name="Voiceover",
        track_kind=TrackKind.AUDIO,
        clip_kind=ClipKind.AUDIO,
        start=0,
        duration=5.5,
        source="Audio/voice.wav",
    )
    builder.add_scene(
        title="Opening",
        start=0,
        duration=5.5,
        narration="A useful fact.",
        clip_ids=[clip.id],
    )

    store.save(builder.build())
    restored = store.load()

    assert restored == builder.build()
    assert restored.duration == 5.5


def test_create_does_not_overwrite_existing_timeline_by_default(tmp_path: Path):
    store = ProjectTimelineStore(tmp_path)
    first = store.create("First")

    second = store.create("Second", frame_rate=60)

    assert second == first
    assert store.load().name == "First"


def test_create_can_explicitly_overwrite_timeline(tmp_path: Path):
    store = ProjectTimelineStore(tmp_path)
    store.create("First")

    replacement = store.create("Second", overwrite=True, frame_rate=60)

    assert replacement.name == "Second"
    assert store.load().frame_rate == 60


def test_load_reports_invalid_json_as_storage_error(tmp_path: Path):
    path = tmp_path / "timeline.json"
    path.write_text("not json", encoding="utf-8")

    with pytest.raises(TimelineStorageError, match="could not read timeline"):
        ProjectTimelineStore(tmp_path).load()


def test_load_reports_invalid_timeline_shape(tmp_path: Path):
    path = tmp_path / "timeline.json"
    path.write_text(json.dumps({"name": "Broken", "frame_rate": 0}), encoding="utf-8")

    with pytest.raises(TimelineStorageError, match="invalid timeline data"):
        ProjectTimelineStore(tmp_path).load()


def test_save_rejects_non_timeline_values(tmp_path: Path):
    with pytest.raises(TypeError, match="timeline must be a Timeline"):
        ProjectTimelineStore(tmp_path).save({"name": "wrong"})
