import json

from common.asset_acquisition import AcquiredAsset, AssetAcquisitionEngine, AssetCandidate
from common.asset_visual_verification import OpenAIImageRelevanceVerifier
from common.verified_asset_acquisition import install_visual_verification


class Provider:
    name = "stock"

    def __init__(self, results):
        self.results = list(results)

    def search(self, query, *, kind, limit):
        return [item for item in self.results if item.kind == kind][:limit]


def candidate(identifier, title, score, url):
    return AssetCandidate(
        provider="stock",
        id=identifier,
        url=url,
        title=title,
        kind="image",
        score=score,
        width=1080,
        height=1920,
    )


def asset_for(tmp_path, identifier="asset", title="Candidate image"):
    image = tmp_path / f"{identifier}.jpg"
    image.write_bytes(b"fake-jpeg-bytes")
    return AcquiredAsset(
        candidate=candidate(
            identifier,
            title,
            1,
            f"https://example.test/{identifier}.jpg",
        ),
        path=image,
    )


def decision(
    *,
    obvious_mismatch=False,
    confidence=0.5,
    hard_negative="none",
    hard_negative_confidence=0.0,
    visual_quality="preferred",
):
    return {
        "obvious_mismatch": obvious_mismatch,
        "confidence": confidence,
        "hard_negative": hard_negative,
        "hard_negative_confidence": hard_negative_confidence,
        "visual_quality": visual_quality,
    }


def test_openai_visual_verifier_is_topic_neutral_and_keeps_plausible_asset(tmp_path):
    asset = asset_for(tmp_path, identifier="tower", title="Paris landmark at sunset")
    requests = []

    def transport(request):
        requests.append(request)
        body = json.loads(request.data.decode("utf-8"))
        content = body["input"][0]["content"]
        prompt = content[0]["text"]
        schema = body["text"]["format"]
        required = schema["schema"]["required"]
        enum = schema["schema"]["properties"]["hard_negative"]["enum"]
        quality_enum = schema["schema"]["properties"]["visual_quality"]["enum"]

        assert body["model"] == "gpt-5-mini"
        assert body["max_output_tokens"] == 800
        assert body["reasoning"] == {"effort": "minimal"}
        assert body["text"]["verbosity"] == "low"
        assert schema["type"] == "json_schema"
        assert schema["strict"] is True
        assert required == [
            "obvious_mismatch",
            "confidence",
            "hard_negative",
            "hard_negative_confidence",
            "visual_quality",
        ]
        assert quality_enum == ["acceptable", "preferred", "weak"]
        assert "wrong_named_subject" in enum
        assert "unrequested_fantasy_creature" in enum
        assert "unrequested_vehicle_or_spacecraft" in enum
        assert "Geography Eiffel Tower Paris sunset" in prompt
        assert "topic-neutral" in prompt
        assert "Never assume a fixed video topic" in prompt
        assert "Big Ben is not the Eiffel Tower" in prompt
        assert "visual_quality" in prompt
        assert content[1]["type"] == "input_image"
        assert content[1]["detail"] == "high"
        assert content[1]["image_url"].startswith("data:image/jpeg;base64,")
        return {"output_text": json.dumps(decision())}

    verifier = OpenAIImageRelevanceVerifier(
        "openai-key",
        model="gpt-5-mini",
        transport=transport,
    )

    assert verifier("Geography Eiffel Tower Paris sunset", asset) is True
    assert verifier.last_quality == "preferred"
    assert len(requests) == 1


def test_openai_visual_verifier_marks_relevant_symbolic_visual_as_weak_not_rejected(tmp_path):
    asset = asset_for(tmp_path, identifier="icons", title="Scientific topic symbolic composition")
    verifier = OpenAIImageRelevanceVerifier(
        "openai-key",
        transport=lambda _request: {
            "output_text": json.dumps(decision(visual_quality="weak"))
        },
    )

    assert verifier("Science photosynthesis leaf process", asset) is True
    assert verifier.last_quality == "weak"
    assert "quality=weak" in verifier.last_decision


def test_openai_visual_verifier_blocks_wrong_named_subject_for_any_topic(tmp_path):
    asset = asset_for(tmp_path, identifier="big-ben", title="London clock tower")
    verifier = OpenAIImageRelevanceVerifier(
        "openai-key",
        transport=lambda _request: {
            "output_text": json.dumps(
                decision(
                    obvious_mismatch=False,
                    confidence=0.45,
                    hard_negative="wrong_named_subject",
                    hard_negative_confidence=0.98,
                )
            )
        },
    )

    assert verifier("Geography Eiffel Tower Paris architecture", asset) is False
    assert "wrong_named_subject" in verifier.last_decision


def test_openai_visual_verifier_blocks_unrequested_fantasy_creature_for_any_topic(tmp_path):
    asset = asset_for(tmp_path, identifier="dragon", title="Decorative fantasy artwork")
    verifier = OpenAIImageRelevanceVerifier(
        "openai-key",
        transport=lambda _request: {
            "output_text": json.dumps(
                decision(
                    hard_negative="unrequested_fantasy_creature",
                    hard_negative_confidence=0.96,
                    visual_quality="weak",
                )
            )
        },
    )

    assert verifier("History Roman aqueduct engineering", asset) is False


def test_openai_visual_verifier_keeps_requested_hard_negative_class(tmp_path):
    asset = asset_for(tmp_path, identifier="rocket", title="Saturn V rocket launch")
    verifier = OpenAIImageRelevanceVerifier(
        "openai-key",
        transport=lambda _request: {"output_text": json.dumps(decision())},
    )

    assert verifier("History Saturn V rocket Apollo 11 launch", asset) is True


def test_openai_visual_verifier_reads_nested_responses_output_and_blocks_obvious_mismatch(tmp_path):
    asset = asset_for(tmp_path)
    verifier = OpenAIImageRelevanceVerifier(
        "openai-key",
        transport=lambda _request: {
            "status": "completed",
            "output": [
                {
                    "type": "message",
                    "content": [
                        {
                            "type": "output_text",
                            "text": json.dumps(
                                decision(obvious_mismatch=True, confidence=0.99)
                            ),
                        }
                    ],
                }
            ],
        },
    )

    assert verifier("Nature glacier ice cave", asset) is False


def test_openai_visual_verifier_does_not_block_low_confidence_general_mismatch(tmp_path):
    asset = asset_for(tmp_path)
    verifier = OpenAIImageRelevanceVerifier(
        "openai-key",
        transport=lambda _request: {
            "output_text": json.dumps(
                decision(obvious_mismatch=True, confidence=0.71)
            )
        },
    )

    assert verifier("Animals blue whale ocean", asset) is True


def test_openai_visual_verifier_requires_very_high_confidence_for_diagram_or_symbol_veto(tmp_path):
    asset = asset_for(tmp_path)
    verifier = OpenAIImageRelevanceVerifier(
        "openai-key",
        transport=lambda _request: {
            "output_text": json.dumps(
                decision(
                    hard_negative="unrequested_generic_diagram",
                    hard_negative_confidence=0.93,
                    visual_quality="weak",
                )
            )
        },
    )

    assert verifier("Technology steam turbine blades", asset) is True
    assert verifier.last_quality == "weak"
    assert "kept:" in verifier.last_decision


def test_openai_visual_verifier_rejects_extremely_confident_unrequested_diagram(tmp_path):
    asset = asset_for(tmp_path)
    verifier = OpenAIImageRelevanceVerifier(
        "openai-key",
        transport=lambda _request: {
            "output_text": json.dumps(
                decision(
                    hard_negative="unrequested_generic_diagram",
                    hard_negative_confidence=0.99,
                    visual_quality="weak",
                )
            )
        },
    )

    assert verifier("Nature redwood forest canopy", asset) is False


def test_visual_verification_rejects_bad_candidate_and_tries_next(tmp_path):
    # Both candidates are lexically relevant to the query, so the higher-scored
    # visually-wrong candidate is tried first and the verifier must reject it.
    bad = candidate(
        "wrong",
        "Golden Gate Bridge decorative artwork",
        100,
        "https://example.test/wrong.jpg",
    )
    good = candidate(
        "bridge",
        "Golden Gate Bridge San Francisco",
        1,
        "https://example.test/bridge.jpg",
    )
    provider = Provider([bad, good])

    engine = AssetAcquisitionEngine(
        [provider],
        downloader=lambda url, path: path.write_bytes(b"image"),
    )
    checked = []

    def verifier(query, asset):
        checked.append((query, asset.candidate.id))
        return asset.candidate.id == "bridge"

    install_visual_verification(engine, verifier)
    result = engine.acquire("Architecture Golden Gate Bridge", tmp_path, attempts=2)

    assert result.candidate.id == "bridge"
    assert checked == [
        ("Architecture Golden Gate Bridge", "wrong"),
        ("Architecture Golden Gate Bridge", "bridge"),
    ]
    assert not any("wrong" in path.name.casefold() for path in tmp_path.iterdir())


def test_visual_verification_prefers_stronger_visual_over_accepted_weak_fallback(tmp_path):
    weak = candidate(
        "weak",
        "Golden Gate Bridge symbolic illustration",
        100,
        "https://example.test/weak.jpg",
    )
    strong = candidate(
        "strong",
        "Golden Gate Bridge San Francisco photograph",
        1,
        "https://example.test/strong.jpg",
    )
    engine = AssetAcquisitionEngine(
        [Provider([weak, strong])],
        downloader=lambda _url, path: path.write_bytes(b"image"),
    )

    class QualityVerifier:
        def __init__(self):
            self.last_quality = "preferred"
            self.checked = []

        def __call__(self, _query, asset):
            self.checked.append(asset.candidate.id)
            self.last_quality = "weak" if asset.candidate.id == "weak" else "preferred"
            return True

    verifier = QualityVerifier()
    install_visual_verification(engine, verifier)
    result = engine.acquire("Architecture Golden Gate Bridge", tmp_path, attempts=2)

    assert result.candidate.id == "strong"
    assert verifier.checked == ["weak", "strong"]
    assert not any("weak" in path.name.casefold() for path in tmp_path.iterdir())


def test_visual_verification_uses_weak_fallback_when_no_stronger_visual_exists(tmp_path):
    weak = candidate(
        "weak",
        "Steam turbine symbolic diagram",
        100,
        "https://example.test/weak.jpg",
    )
    engine = AssetAcquisitionEngine(
        [Provider([weak])],
        downloader=lambda _url, path: path.write_bytes(b"image"),
    )

    class WeakVerifier:
        last_quality = "weak"

        def __call__(self, _query, _asset):
            self.last_quality = "weak"
            return True

    install_visual_verification(engine, WeakVerifier())
    result = engine.acquire("Technology steam turbine", tmp_path, attempts=1)

    assert result.candidate.id == "weak"


def test_visual_verification_error_rejects_candidate_and_tries_next(tmp_path):
    bad = candidate(
        "broken-verifier",
        "Candidate one",
        100,
        "https://example.test/broken.jpg",
    )
    good = candidate(
        "correct",
        "Candidate two",
        1,
        "https://example.test/correct.jpg",
    )
    engine = AssetAcquisitionEngine(
        [Provider([bad, good])],
        downloader=lambda _url, path: path.write_bytes(b"image"),
    )
    checked = []

    def verifier(_query, asset):
        checked.append(asset.candidate.id)
        if asset.candidate.id == "broken-verifier":
            raise RuntimeError("verifier unavailable")
        return True

    install_visual_verification(engine, verifier)
    result = engine.acquire("Nature redwood forest", tmp_path, attempts=2)

    assert result.candidate.id == "correct"
    assert checked == ["broken-verifier", "correct"]
