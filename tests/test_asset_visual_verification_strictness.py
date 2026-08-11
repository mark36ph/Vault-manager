import json

from common.asset_acquisition import AcquiredAsset, AssetCandidate
from common.asset_visual_verification import OpenAIImageRelevanceVerifier


def _asset(tmp_path, identifier="asset", title="Candidate image"):
    image = tmp_path / f"{identifier}.jpg"
    image.write_bytes(b"fake-jpeg-bytes")
    candidate = AssetCandidate(
        provider="stock",
        id=identifier,
        url=f"https://example.test/{identifier}.jpg",
        title=title,
        kind="image",
        score=1,
        width=1080,
        height=1920,
    )
    return AcquiredAsset(candidate=candidate, path=image)


def _decision(**overrides):
    payload = {
        "obvious_mismatch": False,
        "confidence": 0.5,
        "physical_contradiction": False,
        "physical_contradiction_confidence": 0.0,
        "hard_negative": "none",
        "hard_negative_confidence": 0.0,
        "visual_quality": "preferred",
        "visual_style": "literal",
    }
    payload.update(overrides)
    return {"output_text": json.dumps(payload)}


def test_physical_contradiction_rejects_at_stricter_topic_neutral_threshold(tmp_path):
    asset = _asset(tmp_path, "wrong-traits", "Concrete subject with conflicting visible traits")
    verifier = OpenAIImageRelevanceVerifier(
        "openai-key",
        transport=lambda _request: _decision(
            physical_contradiction=True,
            physical_contradiction_confidence=0.84,
            visual_quality="acceptable",
            visual_style="literal",
        ),
    )

    assert verifier("Animals African lion adult male", asset) is False
    assert "threshold 0.82" in verifier.last_decision


def test_ambiguous_physical_contradiction_still_survives(tmp_path):
    asset = _asset(tmp_path, "ambiguous", "Ambiguous reconstruction")
    verifier = OpenAIImageRelevanceVerifier(
        "openai-key",
        transport=lambda _request: _decision(
            physical_contradiction=True,
            physical_contradiction_confidence=0.71,
            visual_quality="acceptable",
            visual_style="representational",
        ),
    )

    assert verifier("History ancient harbor reconstruction", asset) is True


def test_decorative_unrequested_person_is_rejected_at_lower_contextual_threshold(tmp_path):
    asset = _asset(tmp_path, "figure", "Fantasy concept art with prominent human figure")
    verifier = OpenAIImageRelevanceVerifier(
        "openai-key",
        transport=lambda _request: _decision(
            hard_negative="unrequested_person",
            hard_negative_confidence=0.76,
            visual_quality="preferred",
            visual_style="decorative",
        ),
    )

    assert verifier("Science volcanic planet surface atmosphere", asset) is False
    assert "unrequested_person" in verifier.last_decision
    assert "threshold 0.70" in verifier.last_decision


def test_non_decorative_person_keeps_general_hard_negative_threshold(tmp_path):
    asset = _asset(tmp_path, "person", "Documentary person in context")
    verifier = OpenAIImageRelevanceVerifier(
        "openai-key",
        transport=lambda _request: _decision(
            hard_negative="unrequested_person",
            hard_negative_confidence=0.76,
            visual_quality="acceptable",
            visual_style="literal",
        ),
    )

    assert verifier("Technology laboratory equipment closeup", asset) is True
