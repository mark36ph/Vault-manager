from types import SimpleNamespace

from common.named_subject_verification import NamedSubjectVerifier


class StructuredStubVerifier:
    def __init__(
        self,
        *,
        accepted=True,
        subject_visible=False,
        scene_evidence_visible=False,
        identity_mode="named_or_contextual",
        decision="kept",
    ):
        self.accepted = accepted
        self.last_requested_subject_visible = subject_visible
        self.last_requested_scene_evidence_visible = scene_evidence_visible
        self.last_subject_identity_mode = identity_mode
        self.last_subject_uncertain = False
        self.last_decision = decision
        self.last_quality = "preferred"
        self.last_style = "literal"
        self.seen_query = ""

    def __call__(self, query, asset):
        self.seen_query = query
        return self.accepted


def test_model_cannot_promote_common_wombat_subject_out_of_pixel_gate():
    base = StructuredStubVerifier(
        accepted=True,
        subject_visible=False,
        scene_evidence_visible=False,
        identity_mode="named_or_contextual",
        decision=(
            "kept: mismatch=False/0.20, physical_contradiction=False/0.10, "
            "hard_negative=none/0.00, identity_mode=named_or_contextual"
        ),
    )
    verifier = NamedSubjectVerifier(base)

    assert verifier(
        "Nature wombat close up Australia wildlife",
        SimpleNamespace(),
    ) is False
    assert "EXPLICIT-SUBJECT VISUAL REQUIREMENT" in base.seen_query
    assert "NAMED-SUBJECT IDENTITY REQUIREMENT" not in base.seen_query
    assert "explicit subject missing from pixels for wombat" in base.last_decision


def test_duplicated_capitalized_topic_subject_is_not_treated_as_named_entity():
    base = StructuredStubVerifier(
        accepted=True,
        subject_visible=True,
        scene_evidence_visible=False,
        identity_mode="visually_recognizable",
    )
    verifier = NamedSubjectVerifier(base)

    assert verifier(
        "Nature Wombat wombat walking wildlife close up",
        SimpleNamespace(),
    ) is True
    assert "EXPLICIT-SUBJECT VISUAL REQUIREMENT" in base.seen_query
    assert "required concrete subject is 'Wombat'" in base.seen_query
    assert "NAMED-SUBJECT IDENTITY REQUIREMENT" not in base.seen_query


def test_lowercase_single_token_place_with_multiple_context_cues_is_named():
    base = StructuredStubVerifier(
        accepted=True,
        subject_visible=False,
        scene_evidence_visible=False,
        identity_mode="named_or_contextual",
    )
    verifier = NamedSubjectVerifier(base)

    assert verifier(
        "nature yellowstone geothermal caldera waterfall",
        SimpleNamespace(),
    ) is True
    assert "NAMED-SUBJECT IDENTITY REQUIREMENT" in base.seen_query
    assert "requested named subject is 'yellowstone'" in base.seen_query
    assert "EXPLICIT-SUBJECT VISUAL REQUIREMENT" not in base.seen_query


def test_generic_broad_shield_volcano_stays_on_common_subject_gate():
    base = StructuredStubVerifier(
        accepted=True,
        subject_visible=True,
        scene_evidence_visible=False,
        identity_mode="visually_recognizable",
    )
    verifier = NamedSubjectVerifier(base)

    assert verifier(
        "geology broad shield volcano lava field",
        SimpleNamespace(),
    ) is True
    assert "EXPLICIT-SUBJECT VISUAL REQUIREMENT" in base.seen_query
    assert "NAMED-SUBJECT IDENTITY REQUIREMENT" not in base.seen_query
