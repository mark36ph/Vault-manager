import pytest

from common.asset_acquisition import AssetAcquisitionEngine, AssetAcquisitionError, AssetCandidate
from common.named_subject_verification import NamedSubjectVerifier
from common.verified_asset_acquisition import install_visual_verification


class Provider:
    name = "stock"

    def __init__(self, results):
        self.results = list(results)

    def search(self, query, *, kind, limit):
        return [candidate for candidate in self.results if candidate.kind == kind][:limit]


def image_candidate(identifier: str, title: str) -> AssetCandidate:
    return AssetCandidate(
        provider="stock",
        id=identifier,
        url=f"https://example.invalid/{identifier}.jpg",
        kind="image",
        title=title,
        width=1080,
        height=1920,
    )


class SoftWrongSubjectVerifier:
    last_quality = "preferred"
    last_style = "literal"
    last_subject_uncertain = False
    last_decision = ""

    def __call__(self, _query, _asset):
        self.last_decision = (
            "kept: mismatch=True/0.70, physical_contradiction=False/0.00, "
            "hard_negative=other_obvious_unrelated_subject/0.72, "
            "quality=preferred, style=literal"
        )
        return True


class SafeSceneVerifier:
    last_quality = "acceptable"
    last_style = "literal"
    last_subject_uncertain = False
    last_decision = ""

    def __call__(self, _query, _asset):
        self.last_decision = (
            "kept: mismatch=False/0.10, physical_contradiction=False/0.00, "
            "hard_negative=none/0.00, quality=acceptable, style=literal"
        )
        return True


def test_explicit_subject_acquisition_fails_instead_of_using_unrelated_visual(tmp_path):
    # The title deliberately contains the requested word, modeling misleading
    # provider metadata. Pixel verification still reports ancient ruins as an
    # unrelated dominant subject, so production must fail closed.
    wrong = image_candidate("ruins", "wombat ancient civilizations stone ruins")
    engine = AssetAcquisitionEngine(
        [Provider([wrong])],
        downloader=lambda _url, path: path.write_bytes(b"image"),
    )
    install_visual_verification(engine, NamedSubjectVerifier(SoftWrongSubjectVerifier()))

    with pytest.raises(AssetAcquisitionError, match="no visually relevant asset passed verification"):
        engine.acquire("Nature wombat close up Australia wildlife", tmp_path, attempts=1)


def test_explicit_subject_scene_specific_visual_can_still_pass(tmp_path):
    droppings = image_candidate("droppings", "wombat cube shaped droppings ground")
    engine = AssetAcquisitionEngine(
        [Provider([droppings])],
        downloader=lambda _url, path: path.write_bytes(b"image"),
    )
    install_visual_verification(engine, NamedSubjectVerifier(SafeSceneVerifier()))

    result = engine.acquire(
        "Nature wombat droppings cube shaped ground Australia",
        tmp_path,
        attempts=1,
    )

    assert result.candidate.id == "droppings"
