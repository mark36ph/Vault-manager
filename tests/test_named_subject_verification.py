from types import SimpleNamespace

from common.named_subject_verification import (
    NamedSubjectVerifier,
    explicit_subject_phrase,
    named_subject_phrase,
)


def test_named_subject_phrase_preserves_multiword_entities():
    assert named_subject_phrase("nature Mauna Loa Hawaii shield volcano aerial") == "Mauna Loa"
    assert named_subject_phrase("nature Mount Everest Himalayas summit mountain Nepal") == "Mount Everest"
    assert named_subject_phrase("technology James Webb Space Telescope deep field") == "James Webb Space Telescope"
    assert named_subject_phrase("nature Great Barrier Reef Australia coral aerial") == "Great Barrier Reef"
    assert named_subject_phrase("architecture Golden Gate Bridge San Francisco aerial") == "Golden Gate Bridge"


def test_named_subject_phrase_returns_empty_for_generic_lowercase_query():
    assert named_subject_phrase("technology fiber optic cable light data") == ""


def test_explicit_subject_phrase_keeps_named_entity_or_category_anchor():
    assert explicit_subject_phrase("nature Mauna Loa Hawaii shield volcano aerial") == "Mauna Loa"
    assert explicit_subject_phrase("Nature wombat close up Australia wildlife") == "wombat"
    assert explicit_subject_phrase("technology fiber optic cable light data") == "fiber"
    assert explicit_subject_phrase("misty forest landscape") == ""


class StubVerifier:
    def __init__(self, *, accepted=True, uncertain=False, decision="accepted"):
        self.accepted = accepted
        self.last_subject_uncertain = uncertain
        self.last_decision = decision
        self.last_quality = "preferred"
        self.last_style = "literal"
        self.seen_query = ""

    def __call__(self, query, asset):
        self.seen_query = query
        return self.accepted


def asset_with_previous_query(previous_query):
    return SimpleNamespace(
        candidate=SimpleNamespace(
            metadata={"_selection_previous_query": previous_query},
        )
    )


def test_named_subject_verifier_adds_identity_instruction():
    base = StubVerifier()
    verifier = NamedSubjectVerifier(base)

    assert verifier("nature Mauna Loa Hawaii shield volcano aerial", SimpleNamespace()) is True
    assert "NAMED-SUBJECT IDENTITY REQUIREMENT" in base.seen_query
    assert "Mauna Loa" in base.seen_query


def test_common_subject_verifier_adds_pixel_only_identity_instruction():
    base = StubVerifier()
    verifier = NamedSubjectVerifier(base)

    assert verifier("Nature wombat close up Australia wildlife", SimpleNamespace()) is True
    assert "EXPLICIT-SUBJECT VISUAL REQUIREMENT" in base.seen_query
    assert "required concrete subject is 'wombat'" in base.seen_query
    assert "stock titles, tags, URLs, search terms, filenames" in base.seen_query
    assert "Ancient ruins" in base.seen_query
    assert "scene-specific evidence" in base.seen_query
    assert "NAMED-SUBJECT IDENTITY REQUIREMENT" not in base.seen_query


def test_common_subject_soft_keep_with_unrelated_visual_is_overridden():
    base = StubVerifier(
        accepted=True,
        decision=(
            "kept: mismatch=True/0.70, physical_contradiction=False/0.00, "
            "hard_negative=other_obvious_unrelated_subject/0.72, "
            "quality=preferred, style=literal"
        ),
    )
    verifier = NamedSubjectVerifier(base)

    assert verifier("Nature wombat close up Australia wildlife", SimpleNamespace()) is False
    assert "explicit subject contradiction for wombat" in base.last_decision


def test_common_subject_scene_specific_derivative_can_remain_plausible():
    base = StubVerifier(
        accepted=True,
        uncertain=True,
        decision=(
            "kept: mismatch=False/0.20, physical_contradiction=True/0.50, "
            "hard_negative=none/0.00, quality=acceptable, style=literal, subject_uncertain"
        ),
    )
    verifier = NamedSubjectVerifier(base)

    query = "Nature wombat droppings cube shaped ground Australia"
    assert verifier(query, SimpleNamespace()) is True
    assert "scene-specific evidence" in base.seen_query


def test_named_subject_verifier_preserves_uncertain_plausible_fallback():
    base = StubVerifier(accepted=True, uncertain=True)
    verifier = NamedSubjectVerifier(base)

    assert verifier("nature Mount Everest summit mountain", SimpleNamespace()) is True
    assert base.last_subject_uncertain is True


def test_named_subject_verifier_preserves_base_rejection():
    base = StubVerifier(accepted=False)
    verifier = NamedSubjectVerifier(base)

    assert verifier("nature Mount Everest summit mountain", SimpleNamespace()) is False


def test_category_anchored_generic_query_gets_explicit_subject_gate_without_named_gate():
    base = StubVerifier(accepted=True, uncertain=False)
    verifier = NamedSubjectVerifier(base)

    assert verifier("technology fiber optic cable light data", SimpleNamespace()) is True
    assert "EXPLICIT-SUBJECT VISUAL REQUIREMENT" in base.seen_query
    assert "required concrete subject is 'fiber'" in base.seen_query
    assert "NAMED-SUBJECT IDENTITY REQUIREMENT" not in base.seen_query
    assert "SCENE-TRANSITION RELEVANCE REQUIREMENT" not in base.seen_query


def test_transition_context_is_added_for_different_named_scenes():
    base = StubVerifier()
    verifier = NamedSubjectVerifier(base)
    asset = asset_with_previous_query(
        "nature Yellowstone National Park geothermal caldera waterfall"
    )

    current = "nature Mauna Loa Hawaii broad shield volcano lava"
    assert verifier(current, asset) is True

    assert "SCENE-TRANSITION RELEVANCE REQUIREMENT" in base.seen_query
    assert f"CURRENT SCENE: {current}" in base.seen_query
    assert "PREVIOUS SCENE: nature Yellowstone National Park geothermal caldera waterfall" in base.seen_query
    assert "reject it as stale-scene imagery" in base.seen_query
    assert "visible content itself" in base.seen_query
    assert "HARD NAMED-SUBJECT TRANSITION REQUIREMENT" in base.seen_query
    assert "moved from 'Yellowstone National Park' to 'Mauna Loa'" in base.seen_query
    assert "signature-characteristic of the PREVIOUS named subject" in base.seen_query
    assert "Generic topical overlap alone is not enough to pass" in base.seen_query


def test_transition_context_also_applies_to_generic_current_scene_without_hard_named_gate():
    base = StubVerifier()
    verifier = NamedSubjectVerifier(base)
    asset = asset_with_previous_query("animals African lion savanna hunting")

    current = "animals striped big cat stalking through forest"
    assert verifier(current, asset) is True

    assert "EXPLICIT-SUBJECT VISUAL REQUIREMENT" in base.seen_query
    assert "NAMED-SUBJECT IDENTITY REQUIREMENT" not in base.seen_query
    assert "SCENE-TRANSITION RELEVANCE REQUIREMENT" in base.seen_query
    assert f"CURRENT SCENE: {current}" in base.seen_query
    assert "PREVIOUS SCENE: animals African lion savanna hunting" in base.seen_query
    assert "HARD NAMED-SUBJECT TRANSITION REQUIREMENT" not in base.seen_query


def test_same_scene_query_does_not_add_transition_penalty_instruction():
    base = StubVerifier()
    verifier = NamedSubjectVerifier(base)
    current = "nature Mount Everest Himalayas summit mountain Nepal"
    asset = asset_with_previous_query(current)

    assert verifier(current, asset) is True

    assert "NAMED-SUBJECT IDENTITY REQUIREMENT" in base.seen_query
    assert "SCENE-TRANSITION RELEVANCE REQUIREMENT" not in base.seen_query
    assert "HARD NAMED-SUBJECT TRANSITION REQUIREMENT" not in base.seen_query


def test_transition_instruction_does_not_require_previous_named_entity():
    base = StubVerifier()
    verifier = NamedSubjectVerifier(base)
    asset = asset_with_previous_query("nature forest waterfall mist landscape")

    current = "geology broad shield volcano lava field"
    assert verifier(current, asset) is True

    assert "EXPLICIT-SUBJECT VISUAL REQUIREMENT" in base.seen_query
    assert "SCENE-TRANSITION RELEVANCE REQUIREMENT" in base.seen_query
    assert "PREVIOUS SCENE: nature forest waterfall mist landscape" in base.seen_query
    assert "HARD NAMED-SUBJECT TRANSITION REQUIREMENT" not in base.seen_query


def test_different_named_landmarks_get_hard_transition_gate():
    base = StubVerifier()
    verifier = NamedSubjectVerifier(base)
    asset = asset_with_previous_query("architecture Eiffel Tower Paris iron lattice")

    current = "architecture Tokyo Skytree Japan observation tower"
    assert verifier(current, asset) is True

    assert "HARD NAMED-SUBJECT TRANSITION REQUIREMENT" in base.seen_query
    assert "moved from 'Eiffel Tower' to 'Tokyo Skytree'" in base.seen_query
