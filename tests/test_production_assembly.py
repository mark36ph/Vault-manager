import json
from dataclasses import dataclass
from pathlib import Path
from types import SimpleNamespace

import pytest

from common.asset_acquisition import AcquiredAsset, AssetCandidate
from common.content_production import (
    ContentProductionEngine,
    ProductionCheckpointStore,
    ProductionContext,
)
from common.production_assembly import (
    ProductionAssemblyError,
    assemble_timeline,
    json_safe,
)
from timeline import Scene, Timeline


def timeline_with_scenes():
    timeline = Timeline(name="Video", width=1080, height=1920)
    timeline.add_scene(Scene(title="One", start=0, duration=3, narration="one"))
    timeline.add_scene(Scene(title="Two", start=3, duration=4, narration="two"))
    return timeline


def acquired(path, kind="image", provider="pexels"):
    return AcquiredAsset(
        candidate=AssetCandidate(
            provider=provider,
            id=path.stem,
            url="https://example.test/asset",
            kind=kind,
            title=path.stem,
            credit="Creator",
            license="Provider License",
        ),
        path=path,
    )


def test_json_safe_converts_paths_and_dataclasses(tmp_path):
    payload = json_safe(acquired(tmp_path / "image.jpg"))
    assert payload["path"] == str(tmp_path / "image.jpg")
    assert payload["candidate"]["provider"] == "pexels"


def test_checkpoint_saves_acquired_asset_results(tmp_path):
    context = ProductionContext({}, tmp_path, {})
    context.image_prompts = [acquired(tmp_path / "image.jpg")]
    context.completed_stages = ["image_prompts"]
    path = ProductionCheckpointStore(tmp_path).save(context)
    payload = json.loads(path.read_text(encoding="utf-8"))
    assert payload["image_prompts"][0]["path"].endswith("image.jpg")


def test_checkpoint_restores_serialized_asset_results(tmp_path):
    context = ProductionContext({}, tmp_path, {})
    context.image_prompts = [acquired(tmp_path / "image.jpg")]
    context.completed_stages = ["image_prompts"]
    store = ProductionCheckpointStore(tmp_path)
    store.save(context)
    restored = store.load_into(ProductionContext({}, tmp_path, {}))
    assert restored.image_prompts[0]["candidate"]["kind"] == "image"


def test_checkpoint_round_trips_completed_timeline(tmp_path):
    context = ProductionContext({}, tmp_path, {})
    context.timeline = timeline_with_scenes()
    context.completed_stages = [
        "research", "facts", "script", "image_prompts", "voice", "timeline"
    ]
    store = ProductionCheckpointStore(tmp_path)
    store.save(context)

    restored = store.load_into(ProductionContext({}, tmp_path, {}))

    assert restored.timeline is not None
    assert restored.timeline.name == "Video"
    assert len(restored.timeline.scenes) == 2
    assert "timeline" in restored.completed_stages


def test_legacy_checkpoint_rebuilds_missing_completed_timeline(tmp_path):
    checkpoint = tmp_path / "production_checkpoint.json"
    checkpoint.write_text(json.dumps({
        "script": "Sentence one. Sentence two.",
        "image_prompts": [],
        "voice": None,
        "completed_stages": [
            "research", "facts", "script", "image_prompts", "voice", "timeline"
        ],
    }), encoding="utf-8")

    restored = ProductionCheckpointStore(tmp_path).load_into(
        ProductionContext({}, tmp_path, {})
    )

    assert restored.timeline is None
    assert restored.completed_stages == [
        "research", "facts", "script", "image_prompts", "voice"
    ]


def test_assemble_timeline_adds_visual_for_each_scene(tmp_path):
    timeline = assemble_timeline(
        timeline_with_scenes(),
        [acquired(tmp_path / "one.jpg"), acquired(tmp_path / "two.mp4", "video")],
    )
    visuals = timeline.get_track("Visuals")
    assert visuals is not None
    assert [clip.source for clip in visuals.clips] == [
        str((tmp_path / "one.jpg").resolve()),
        str((tmp_path / "two.mp4").resolve()),
    ]
    assert all(scene.clip_ids for scene in timeline.scenes)


def test_assemble_timeline_rejects_too_few_acquired_visuals(tmp_path):
    with pytest.raises(ProductionAssemblyError, match="1 asset.*2 scene"):
        assemble_timeline(
            timeline_with_scenes(),
            [acquired(tmp_path / "only.jpg")],
        )


def test_assemble_timeline_rejects_missing_acquired_visual(tmp_path):
    project = tmp_path / "Project"
    project.mkdir()
    first = project / "first.jpg"
    first.write_bytes(b"image")
    missing = project / "missing.jpg"

    with pytest.raises(ProductionAssemblyError, match="missing from disk"):
        assemble_timeline(
            timeline_with_scenes(),
            [acquired(first), acquired(missing)],
            project_folder=project,
        )


def test_assemble_timeline_records_unique_visual_count(tmp_path):
    shared = acquired(tmp_path / "shared.jpg")
    timeline = assemble_timeline(
        timeline_with_scenes(),
        [shared, shared],
    )

    assert timeline.metadata["production_assets"] == 2
    assert timeline.metadata["production_unique_assets"] == 1


def test_assemble_timeline_accepts_restored_dictionary_assets(tmp_path):
    asset = json_safe(acquired(tmp_path / "image.jpg", provider="pixabay"))
    timeline = assemble_timeline(timeline_with_scenes(), [asset, asset])
    clip = timeline.get_track("Visuals").clips[0]
    assert clip.metadata["provider"] == "pixabay"
    assert clip.metadata["credit"] == "Creator"


def test_assemble_timeline_adds_narration_track(tmp_path):
    voice = tmp_path / "narration.mp3"
    timeline = assemble_timeline(timeline_with_scenes(), [], str(voice))
    narration = timeline.get_track("Narration")
    assert narration is not None
    assert narration.clips[0].source == str(voice.resolve())
    assert narration.clips[0].duration == 7


def test_assemble_timeline_resolves_relative_project_media_paths(tmp_path, monkeypatch):
    monkeypatch.chdir(tmp_path)
    project = Path("In Progress") / "Tower"
    image = project / "Assets" / "Acquired" / "tower.jpg"
    voice = project / "Voice" / "narration.mp3"
    image.parent.mkdir(parents=True)
    voice.parent.mkdir(parents=True)
    image.write_bytes(b"image")
    voice.write_bytes(b"voice")

    timeline = assemble_timeline(
        timeline_with_scenes(),
        [acquired(image), acquired(image)],
        str(voice),
        project_folder=project,
    )

    assert Path(timeline.get_track("Visuals").clips[0].source).is_absolute()
    assert Path(timeline.get_track("Visuals").clips[0].source).is_file()
    assert Path(timeline.get_track("Narration").clips[0].source).is_absolute()
    assert Path(timeline.get_track("Narration").clips[0].source).is_file()


def test_assemble_timeline_does_not_duplicate_narration(tmp_path):
    voice = str(tmp_path / "narration.mp3")
    timeline = timeline_with_scenes()
    assemble_timeline(timeline, [], voice)
    assemble_timeline(timeline, [], voice)
    assert len(timeline.get_track("Narration").clips) == 1


def test_voice_stage_can_be_disabled_without_failing(tmp_path):
    engine = ContentProductionEngine({
        "research": lambda context: "research",
        "facts": lambda context: "facts",
        "script": lambda context: "Sentence one. Sentence two.",
        "image_prompts": lambda context: [],
    })
    result = engine.run(
        {"title": "Fact"},
        tmp_path,
        {},
        stop_after="voice",
        resume=False,
    )
    assert "voice" in result.completed
    assert result.context.voice is None
    assert "Narration generation is disabled" in result.context.warnings


def test_result_started_at_is_real_timestamp(tmp_path):
    engine = ContentProductionEngine({
        "research": lambda context: "research",
        "facts": lambda context: "facts",
        "script": lambda context: "script",
        "image_prompts": lambda context: [],
    })
    result = engine.run({"title": "Fact"}, tmp_path, {}, stop_after="research", resume=False)
    assert "T" in result.started_at
    assert result.started_at.endswith("+00:00")
