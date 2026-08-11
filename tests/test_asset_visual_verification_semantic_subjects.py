import json

from common.asset_acquisition import AcquiredAsset, AssetCandidate
from common.asset_visual_verification import OpenAIImageRelevanceVerifier


def _asset(tmp_path, *, title="Candidate image"):
    path = tmp_path / "candidate.jpg"
    path.write_bytes(b"fake-jpeg-bytes")
    return AcquiredAsset(
        candidate=AssetCandidate(
            provider="stock",
            id="semantic-candidate",
            url="https://example.test/candidate.jpg",
            title=title,
            kind="image",
            score=1,
            width=1080,
            height=1920,
        ),
        path=path,
    )


def _decision(*, hard_negative="none", hard_negative_confidence=0.0):
    return {
        "obvious_mismatch": False,
        "confidence": 0.2,
        "physical_contradiction": False,
        "physical_contradiction_confidence": 0.0,
        "hard_negative": hard_negative,
        "hard_negative_confidence": hard_negative_confidence,
        "visual_quality": "acceptable",
        "visual_style": "literal",
    }


def test_wrong_named_subject_uses_stricter_semantic_threshold(tmp_path):
    verifier = OpenAIImageRelevanceVerifier(
        "openai-key",
        transport=lambda _request: {
            "output_text": json.dumps(
                _decision(
                    hard_negative="wrong_named_subject",
                    hard_negative_confidence=0.75,
                )
            )
        },
    )

    assert verifier("Astronomy Venus planet sunrise atmosphere", _asset(tmp_path)) is False
    assert "wrong_named_subject" in verifier.last_decision
    assert "threshold 0.72" in verifier.last_decision


def test_semantic_disambiguation_instruction_uses_full_query_not_shared_keyword(tmp_path):
    captured = {}

    def transport(request):
        body = json.loads(request.data.decode("utf-8"))
        captured["instruction"] = body["input"][0]["content"][0]["text"]
        return {"output_text": json.dumps(_decision())}

    verifier = OpenAIImageRelevanceVerifier("openai-key", transport=transport)

    assert verifier(
        "Astronomy Venus planet retrograde rotation",
        _asset(tmp_path, title="Venus flytrap plant macro"),
    ) is True

    instruction = captured["instruction"]
    assert "FULL scene query" in instruction
    assert "shared name or keyword is not evidence" in instruction
    assert "Venus flytrap for the planet Venus" in instruction
    assert "Jaguar car for a jaguar animal" in instruction
    assert "Amazon company/logo for the Amazon River" in instruction
