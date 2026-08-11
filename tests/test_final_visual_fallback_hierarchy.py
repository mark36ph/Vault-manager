import json

from common.asset_acquisition import AcquiredAsset, AssetAcquisitionEngine, AssetCandidate
from common.asset_visual_verification import OpenAIImageRelevanceVerifier
from common.verified_asset_acquisition import install_visual_verification


def candidate(identifier, title, score=100):
    return AssetCandidate(
        provider="stock",
        id=identifier,
        url=f"https://example.test/{identifier}.jpg",
        title=title,
        kind="image",
        score=score,
        width=1080,
        height=1920,
    )


class QueryProvider:
    name = "stock"

    def __init__(self, original_query, original_results, fallback_results):
        self.original_query = original_query
        self.original_results = list(original_results)
        self.fallback_results = list(fallback_results)

    def search(self, query, *, kind, limit):
        items = self.original_results if query == self.original_query else self.fallback_results
        return [item for item in items if item.kind == kind][:limit]


def test_decorative_original_result_is_deferred_for_factual_fallback(tmp_path):
    query = "Nature redwood forest canopy"
    decorative = candidate("decorative", "Redwood forest symbolic fantasy composition")
    factual = candidate("factual", "Redwood forest trees documentary photograph")
    engine = AssetAcquisitionEngine(
        [QueryProvider(query, [decorative], [factual])],
        downloader=lambda _url, path: path.write_bytes(b"image"),
    )

    class Verifier:
        last_quality = "preferred"
        last_style = "literal"
        last_subject_uncertain = False

        def __call__(self, _query, asset):
            if asset.candidate.id == "decorative":
                self.last_quality = "preferred"
                self.last_style = "decorative"
                self.last_subject_uncertain = False
            else:
                self.last_quality = "weak"
                self.last_style = "representational"
                self.last_subject_uncertain = False
            return True

    install_visual_verification(engine, Verifier())
    result = engine.acquire(query, tmp_path, attempts=1)

    assert result.candidate.id == "factual"
    assert not any("decorative" in path.name for path in tmp_path.iterdir())


def test_subject_uncertain_original_result_is_deferred_for_certain_fallback(tmp_path):
    query = "Transport World War I biplane flight"
    uncertain = candidate("uncertain", "Biplane-like aircraft historical image")
    certain = candidate("certain", "World War I biplane archival photograph")
    engine = AssetAcquisitionEngine(
        [QueryProvider(query, [uncertain], [certain])],
        downloader=lambda _url, path: path.write_bytes(b"image"),
    )

    class Verifier:
        last_quality = "acceptable"
        last_style = "literal"
        last_subject_uncertain = False

        def __call__(self, _query, asset):
            self.last_quality = "acceptable"
            self.last_style = "literal"
            self.last_subject_uncertain = asset.candidate.id == "uncertain"
            return True

    install_visual_verification(engine, Verifier())
    result = engine.acquire(query, tmp_path, attempts=1)

    assert result.candidate.id == "certain"
    assert not any("uncertain" in path.name for path in tmp_path.iterdir())


def test_verifier_exposes_moderate_physical_doubt_as_subject_uncertain(tmp_path):
    image = tmp_path / "candidate.jpg"
    image.write_bytes(b"fake-jpeg-bytes")
    asset = AcquiredAsset(
        candidate=candidate("candidate", "Broadly related but doubtful subject"),
        path=image,
    )

    response = {
        "obvious_mismatch": False,
        "confidence": 0.4,
        "physical_contradiction": True,
        "physical_contradiction_confidence": 0.61,
        "hard_negative": "none",
        "hard_negative_confidence": 0.0,
        "visual_quality": "acceptable",
        "visual_style": "literal",
    }
    verifier = OpenAIImageRelevanceVerifier(
        "openai-key",
        transport=lambda _request: {"output_text": json.dumps(response)},
    )

    assert verifier("Architecture stone arch bridge", asset) is True
    assert verifier.last_subject_uncertain is True
    assert "subject_uncertain" in verifier.last_decision
