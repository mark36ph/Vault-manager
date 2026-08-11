from pathlib import Path

from common.asset_acquisition import AcquiredAsset, AssetCandidate
import common.mixed_asset_acquisition as mixed


def test_scene_caption_specs_preserve_imported_timing():
    value = """0-6 sec
YELLOWSTONE IS HUGE

6-12 sec
A GIANT CALDERA

12-18 sec
BUT NOT THE BIGGEST
"""

    assert mixed._scene_caption_specs(value) == [
        (0.0, 6.0, "YELLOWSTONE IS HUGE"),
        (6.0, 12.0, "A GIANT CALDERA"),
        (12.0, 18.0, "BUT NOT THE BIGGEST"),
    ]


def test_install_mixed_visual_acquisition_routes_normal_image_stage(monkeypatch, tmp_path):
    class Engine:
        def __init__(self):
            self.calls = []

        def acquire_many(self, queries, destination_folder, *, kind="image", **options):
            self.calls.append((list(queries), Path(destination_folder), kind, options))
            return ["original"]

    engine = Engine()
    verifier = object()
    expected = [
        AcquiredAsset(
            AssetCandidate(
                provider="pexels",
                id="video-1",
                url="https://example.invalid/video.mp4",
                kind="video",
                title="literal subject footage",
            ),
            tmp_path / "video.mp4",
        )
    ]

    def fake_mixed(engine_arg, verifier_arg, queries, destination_folder, **options):
        assert engine_arg is engine
        assert verifier_arg is verifier
        assert list(queries) == ["nature Mauna Loa Hawaii shield volcano"]
        assert Path(destination_folder) == tmp_path
        assert options["unique"] is True
        return expected

    monkeypatch.setattr(mixed, "acquire_mixed_many", fake_mixed)
    mixed.install_mixed_visual_acquisition(engine, verifier)

    result = engine.acquire_many(
        ["nature Mauna Loa Hawaii shield volcano"],
        tmp_path,
        kind="image",
        unique=True,
    )

    assert result == expected
    assert engine.calls == []


def test_install_mixed_visual_acquisition_preserves_explicit_video_mode(tmp_path):
    class Engine:
        def __init__(self):
            self.calls = []

        def acquire_many(self, queries, destination_folder, *, kind="image", **options):
            self.calls.append((list(queries), Path(destination_folder), kind, options))
            return ["original"]

    engine = Engine()
    mixed.install_mixed_visual_acquisition(engine, object())

    result = engine.acquire_many(["ocean shark"], tmp_path, kind="video", unique=True)

    assert result == ["original"]
    assert engine.calls == [(["ocean shark"], tmp_path, "video", {"unique": True})]


def test_wrap_caption_keeps_short_phrases_together():
    assert mixed._wrap_caption("MAUNA LOA IS ENORMOUS", width=24) == "MAUNA LOA IS ENORMOUS"
