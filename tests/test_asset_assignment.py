import pytest

from timeline import (
    Asset,
    AssetAssignmentEngine,
    AssetAssignmentError,
    AssetKind,
    AssetStatus,
    ProjectTimelineStore,
    SceneBuilder,
)


def make_timeline():
    return SceneBuilder().build("First scene.\n\nSecond scene.")


def test_asset_defaults_and_serialization():
    asset = Asset(kind=AssetKind.IMAGE, path="assets/image.jpg")
    restored = Asset.from_dict(asset.to_dict())
    assert restored == asset
    assert restored.status is AssetStatus.PENDING


def test_asset_rejects_negative_duration():
    with pytest.raises(ValueError, match="duration"):
        Asset(kind="video", duration=-1)


def test_assign_asset_to_scene():
    timeline = make_timeline()
    engine = AssetAssignmentEngine(timeline)
    scene = timeline.scenes[0]
    assigned = engine.assign(scene.id, Asset(kind="image", path="image.jpg"))
    assert assigned.status is AssetStatus.ASSIGNED
    assert engine.assets_for_scene(scene.id) == [assigned]


def test_assign_rejects_duplicate_id_across_scenes():
    timeline = make_timeline()
    engine = AssetAssignmentEngine(timeline)
    asset = Asset(kind="image", path="image.jpg", id="same")
    engine.assign(timeline.scenes[0].id, asset)
    with pytest.raises(AssetAssignmentError, match="already assigned"):
        engine.assign(timeline.scenes[1].id, asset)


def test_remove_asset():
    timeline = make_timeline()
    engine = AssetAssignmentEngine(timeline)
    scene = timeline.scenes[0]
    assigned = engine.assign(scene.id, Asset(kind="audio", path="voice.wav"))
    removed = engine.remove(scene.id, assigned.id)
    assert removed.id == assigned.id
    assert engine.assets_for_scene(scene.id) == []


def test_remove_unknown_asset_fails():
    timeline = make_timeline()
    engine = AssetAssignmentEngine(timeline)
    with pytest.raises(AssetAssignmentError, match="not assigned"):
        engine.remove(timeline.scenes[0].id, "missing")


def test_move_asset_between_scenes():
    timeline = make_timeline()
    engine = AssetAssignmentEngine(timeline)
    first, second = timeline.scenes
    asset = engine.assign(first.id, Asset(kind="video", path="clip.mp4"))
    moved = engine.move(asset.id, second.id)
    assert engine.assets_for_scene(first.id) == []
    assert engine.assets_for_scene(second.id) == [moved]


def test_unknown_scene_fails():
    engine = AssetAssignmentEngine(make_timeline())
    with pytest.raises(AssetAssignmentError, match="unknown scene"):
        engine.assign("missing", Asset(kind="image"))


def test_validation_reports_assigned_asset_without_path():
    timeline = make_timeline()
    engine = AssetAssignmentEngine(timeline)
    engine.assign(timeline.scenes[0].id, Asset(kind="image"))
    assert any("has no path" in issue for issue in engine.validate())


def test_validation_reports_duplicate_ids_in_legacy_data():
    timeline = make_timeline()
    raw = Asset(kind="image", path="image.jpg", id="duplicate").to_dict()
    timeline.scenes[0].metadata["assets"] = [raw]
    timeline.scenes[1].metadata["assets"] = [raw]
    assert "duplicate asset id: duplicate" in AssetAssignmentEngine(timeline).validate()


def test_assignments_survive_project_storage_round_trip(tmp_path):
    timeline = make_timeline()
    engine = AssetAssignmentEngine(timeline)
    scene = timeline.scenes[0]
    assigned = engine.assign(scene.id, Asset(kind="subtitle", path="captions.srt"))
    store = ProjectTimelineStore(tmp_path)
    store.save(timeline)
    loaded = store.load()
    loaded_assets = AssetAssignmentEngine(loaded).assets_for_scene(scene.id)
    assert loaded_assets[0].id == assigned.id
    assert loaded_assets[0].kind is AssetKind.SUBTITLE
