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
    physical_contradiction=False,
    physical_contradiction_confidence=0.0,
    hard_negative="none",
    hard_negative_confidence=0.0,
    visual_quality="preferred",
    visual_style="literal",
    requested_subject_visible=True,
    requested_scene_evidence_visible=False,
    explicit_subject_contradiction=False,
    explicit_subject_confidence=0.0,
    subject_identity_mode="visually_recognizable",
):
    return {
        "obvious_mismatch": obvious_mismatch,
        "confidence": confidence,
        "physical_contradiction": physical_contradiction,
        "physical_contradiction_confidence": physical_contradiction_confidence,
        "hard_negative": hard_negative,
        "hard_negative_confidence": hard_negative_confidence,
        "visual_quality": visual_quality,
        "visual_style": visual_style,
        "requested_subject_visible": requested_subject_visible,
        "requested_scene_evidence_visible": requested_scene_evidence_visible,
        "explicit_subject_contradiction": explicit_subject_contradiction,
        "explicit_subject_confidence": explicit_subject_confidence,
        "subject_identity_mode": subject_identity_mode,
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
        style_enum = schema["schema"]["properties"]["visual_style"]["enum"]
        identity_enum = schema["schema"]["properties"]["subject_identity_mode"]["enum"]

        assert body["model"] == "gpt-5-mini"
        assert body["max_output_tokens"] == 800
        assert body["reasoning"] == {"effort": "minimal"}
        assert body["text"]["verbosity"] == "low"
        assert schema["type"] == "json_schema"
        assert schema["strict"] is True
        assert required == [
            "obvious_mismatch",
            "confidence",
            "physical_contradiction",
            "physical_contradiction_confidence",
            "hard_negative",
            "hard_negative_confidence",
            "visual_quality",
            "visual_style",
            "requested_subject_visible",
            "requested_scene_evidence_visible",
            "explicit_subject_contradiction",
            "explicit_subject_confidence",
            "subject_identity_mode",
        ]
        assert quality_enum == ["acceptable", "preferred", "weak"]
        assert style_enum == ["decorative", "literal", "representational"]
        assert identity_enum == ["named_or_contextual", "visually_recognizable"]
        assert "wrong_named_subject" in enum
        assert "unrequested_fantasy_creature" in enum
        assert "unrequested_vehicle_or_spacecraft" in enum
        assert "Geography Eiffel Tower Paris sunset" in prompt
        assert "topic-neutral" in prompt
        assert "Never assume a fixed video topic" in prompt
        assert "physical_contradiction" in prompt
        assert "defining visible traits" in prompt
        assert "Big Ben is not the Eiffel Tower" in prompt
        assert "visual_quality" in prompt
        assert "visual_style" in prompt
        assert "requested_subject_visible" in prompt
        assert "requested_scene_evidence_visible" in prompt
        assert "subject_identity_mode" in prompt
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
    assert verifier.last_style == "literal"
    assert len(requests) == 1


def test_explicit_subject_gate_rejects_when_neither_subject_nor_scene_evidence_is_visible(tmp_path):
    asset = asset_for(tmp_path, identifier="ruins", title="Ancient stone ruins")
    verifier = OpenAIImageRelevanceVerifier(
        "openai-key",
        transport=lambda _request: {
            "output_text": json.dumps(
                decision(
                    requested_subject_visible=False,
                    requested_scene_evidence_visible=False,
                    explicit_subject_contradiction=False,
                    explicit_subject_confidence=0.2,
                    subject_identity_mode="visually_recognizable",
                )
            )
        },
    )

    query = (
        "Nature wombat close up Australia wildlife\n\n"
        "EXPLICIT-SUBJECT VISUAL REQUIREMENT: The required concrete subject is 'wombat'."
    )
    assert verifier(query, asset) is False
    assert "explicit subject missing from pixels" in verifier.last_decision


def test_explicit_named_contextual_subject_can_survive_missing_unique_pixel_proof(tmp_path):
    asset = asset_for(tmp_path, identifier="geothermal", title="Geothermal landscape")
    verifier = OpenAIImageRelevanceVerifier(
        "openai-key",
        transport=lambda _request: {
            "output_text": json.dumps(
                decision(
                    requested_subject_visible=False,
                    requested_scene_evidence_visible=False,
                    explicit_subject_contradiction=False,
                    explicit_subject_confidence=0.93,
                    subject_identity_mode="named_or_contextual",
                    visual_quality="acceptable",
                )
            )
        },
    )

    query = (
        "Nature yellowstone geothermal caldera landscape\n\n"
        "EXPLICIT-SUBJECT VISUAL REQUIREMENT: The required concrete subject is 'yellowstone'."
    )
    assert verifier(query, asset) is True
    assert verifier.last_subject_identity_mode == "named_or_contextual"
    assert "identity_mode=named_or_contextual" in verifier.last_decision


def test_explicit_subject_gate_accepts_requested_scene_specific_evidence(tmp_path):
    asset = asset_for(tmp_path, identifier="droppings", title="Cube shaped droppings on ground")
    verifier = OpenAIImageRelevanceVerifier(
        "openai-key",
        transport=lambda _request: {
            "output_text": json.dumps(
                decision(
                    requested_subject_visible=False,
                    requested_scene_evidence_visible=True,
                    explicit_subject_contradiction=False,
                    explicit_subject_confidence=0.05,
                )
            )
        },
    )

    query = (
        "Nature wombat droppings cube shaped ground Australia\n\n"
        "EXPLICIT-SUBJECT VISUAL REQUIREMENT: The required concrete subject is 'wombat'."
    )
    assert verifier(query, asset) is True
    assert verifier.last_requested_scene_evidence_visible is True


def test_explicit_subject_gate_rejects_structured_contradiction(tmp_path):
    asset = asset_for(tmp_path, identifier="temple", title="Ancient temple")
    verifier = OpenAIImageRelevanceVerifier(
        "openai-key",
        transport=lambda _request: {
            "output_text": json.dumps(
                decision(
                    requested_subject_visible=True,
                    explicit_subject_contradiction=True,
                    explicit_subject_confidence=0.92,
                )
            )
        },
    )

    query = (
        "Nature wombat close up Australia wildlife\n\n"
        "EXPLICIT-SUBJECT VISUAL REQUIREMENT: The required concrete subject is 'wombat'."
    )
    assert verifier(query, asset) is False
    assert "explicit subject contradiction" in verifier.last_decision


def test_openai_visual_verifier_marks_relevant_symbolic_visual_as_weak_decorative(tmp_path):
    asset = asset_for(tmp_path, identifier="icons", title="Scientific topic symbolic composition")
    verifier = OpenAIImageRelevanceVerifier(
        "openai-key",
        transport=lambda _request: {
            "output_text": json.dumps(
                decision(visual_quality="weak", visual_style="decorative")
            )
        },
    )

    assert verifier("Science photosynthesis leaf process", asset) is True
    assert verifier.last_quality == "weak"
    assert verifier.last_style == "decorative"
    assert "quality=weak" in verifier.last_decision
    assert "style=decorative" in verifier.last_decision


def test_openai_visual_verifier_blocks_high_confidence_physical_contradiction_for_any_topic(tmp_path):
    asset = asset_for(tmp_path, identifier="wrong-type", title="Candidate with conflicting visible traits")
    verifier = OpenAIImageRelevanceVerifier(
        "openai-key",
        transport=lambda _request: {
            "output_text": json.dumps(
                decision(
                    physical_contradiction=True,
                    physical_contradiction_confidence=0.97,
                    visual_quality="acceptable",
                    visual_style="literal",
                )
            )
        },
    )

    assert verifier("Transport World War I biplane in flight", asset) is False
    assert "physical contradiction" in verifier.last_decision


def test_openai_visual_verifier_keeps_low_confidence_physical_contradiction(tmp_path):
    asset = asset_for(tmp_path, identifier="ambiguous", title="Ambiguous historical reconstruction")
    verifier = OpenAIImageRelevanceVerifier(
        "openai-key",
        transport=lambda _request: {
            "output_text": json.dumps(
                decision(
                    physical_contradiction=True,
                    physical_contradiction_confidence=0.62,
                    visual_quality="acceptable",
                    visual_style="representational",
                )
            )
        },
    )

    assert verifier("History Roman aqueduct construction", asset) is True
    assert "physical_contradiction=True/0.62" in verifier.last_decision


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
                    visual_style="decorative",
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
                    visual_style="decorative",
                )
            )
        },
    )

    assert verifier("Technology steam turbine blades", asset) is True
    assert verifier.last_quality == "weak"
    assert verifier.last_style == "decorative"
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
                    visual_style="decorative",
                )
            )
        },
    )

    assert verifier("Nature redwood forest canopy", asset) is False


def test_visual_verification_rejects_bad_candidate_and_tries_next(tmp_path):
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
            self.last_style = "literal"
            self.checked = []

        def __call__(self, _query, asset):
            self.checked.append(asset.candidate.id)
            if asset.candidate.id == "weak":
                self.last_quality = "weak"
                self.last_style = "decorative"
            else:
                self.last_quality = "preferred"
                self.last_style = "literal"
            return True

    verifier = QualityVerifier()
    install_visual_verification(engine, verifier)
    result = engine.acquire("Architecture Golden Gate Bridge", tmp_path, attempts=2)

    assert result.candidate.id == "strong"
    assert verifier.checked == ["weak", "strong"]
    assert not any("weak" in path.name.casefold() for path in tmp_path.iterdir())


def test_visual_verification_can_prefer_acceptable_literal_over_preferred_decorative(tmp_path):
    decorative = candidate(
        "decorative",
        "Redwood forest artistic emblem",
        100,
        "https://example.test/decorative.jpg",
    )
    literal = candidate(
        "literal",
        "Redwood forest trees photograph",
        1,
        "https://example.test/literal.jpg",
    )
    engine = AssetAcquisitionEngine(
        [Provider([decorative, literal])],
        downloader=lambda _url, path: path.write_bytes(b"image"),
    )

    class StyleVerifier:
        last_quality = "preferred"
        last_style = "decorative"

        def __call__(self, _query, asset):
            if asset.candidate.id == "decorative":
                self.last_quality = "preferred"
                self.last_style = "decorative"
            else:
                self.last_quality = "acceptable"
                self.last_style = "literal"
            return True

    install_visual_verification(engine, StyleVerifier())
    result = engine.acquire("Nature redwood forest", tmp_path, attempts=2)

    assert result.candidate.id == "literal"


def test_visual_verification_weak_factual_visual_beats_preferred_decorative(tmp_path):
    decorative = candidate(
        "decorative",
        "Steam turbine futuristic concept art",
        100,
        "https://example.test/decorative.jpg",
    )
    factual = candidate(
        "factual",
        "Steam turbine machinery reference image",
        1,
        "https://example.test/factual.jpg",
    )
    engine = AssetAcquisitionEngine(
        [Provider([decorative, factual])],
        downloader=lambda _url, path: path.write_bytes(b"image"),
    )

    class StyleVerifier:
        last_quality = "preferred"
        last_style = "decorative"

        def __call__(self, _query, asset):
            if asset.candidate.id == "decorative":
                self.last_quality = "preferred"
                self.last_style = "decorative"
            else:
                self.last_quality = "weak"
                self.last_style = "representational"
            return True

    install_visual_verification(engine, StyleVerifier())
    result = engine.acquire("Technology steam turbine", tmp_path, attempts=2)

    assert result.candidate.id == "factual"


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
        last_style = "decorative"

        def __call__(self, _query, _asset):
            self.last_quality = "weak"
            self.last_style = "decorative"
            return True

    install_visual_verification(engine, WeakVerifier())
    result = engine.acquire("Technology steam turbine", tmp_path, attempts=1)

    assert result.candidate.id == "weak"


def test_visual_verification_defers_reusing_excluded_asset_until_last_resort(tmp_path):
    repeated = candidate(
        "repeat",
        "Golden Gate Bridge photograph",
        100,
        "https://example.test/repeat.jpg",
    )
    engine = AssetAcquisitionEngine(
        [Provider([repeated])],
        downloader=lambda _url, path: path.write_bytes(b"image"),
    )

    class ReuseVerifier:
        last_quality = "preferred"
        last_style = "literal"

        def __init__(self):
            self.checked = []

        def __call__(self, _query, asset):
            self.checked.append(asset.candidate.id)
            return True

    verifier = ReuseVerifier()
    install_visual_verification(engine, verifier)
    result = engine.acquire(
        "Architecture Golden Gate Bridge",
        tmp_path,
        attempts=1,
        excluded={repeated.url},
    )

    assert result.candidate.id == "repeat"
    assert verifier.checked == ["repeat"]


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
