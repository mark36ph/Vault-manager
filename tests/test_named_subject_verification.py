from types import SimpleNamespace

from common.named_subject_verification import NamedSubjectVerifier, named_subject_phrase


def test_named_subject_phrase_preserves_multiword_entities():
    assert named_subject_phrase("nature Mauna Loa Hawaii shield volcano aerial") == "Mauna Loa"
    assert named_subject_phrase("nature Mount Everest Himalayas summit mountain Nepal") == "Mount Everest"
    assert named_subject_phrase("technology James Webb Space Telescope deep field") == "James Webb Space Telescope"
    assert named_subject_phrase("nature Great Barrier Reef Australia coral aerial") == "Great Barrier Reef"
    assert named_subject_phrase("architecture Golden Gate Bridge San Francisco aerial") == "Golden Gate Bridge"


def test_named_subject_phrase_returns_empty_for_generic_lowercase_query():
    assert named_subject_phrase("technology fiber optic cable light data") == ""


class StubVerifier:
    def __init__(self, *, accepted=True, uncertain=False):
        self.accepted = accepted
        self.last_subject_uncertain = uncertain
        self.last_decision = "accepted"
        self.last_quality = "preferred"
        self.last_style = "literal"
        self.seen_query = ""

    def __call__(self, query, asset):
        self.seen_query = query
        return self.accepted


def test_named_subject_verifier_adds_identity_instruction():
    base = StubVerifier()
    verifier = NamedSubjectVerifier(base)

    assert verifier("nature Mauna Loa Hawaii shield volcano aerial", SimpleNamespace()) is True
    assert "NAMED-SUBJECT IDENTITY REQUIREMENT" in base.seen_query
    assert "Mauna Loa" in base.seen_query


def test_named_subject_verifier_preserves_uncertain_plausible_fallback():
    base = StubVerifier(accepted=True, uncertain=True)
    verifier = NamedSubjectVerifier(base)

    assert verifier("nature Mount Everest summit mountain", SimpleNamespace()) is True
    assert base.last_subject_uncertain is True


def test_named_subject_verifier_preserves_base_rejection():
    base = StubVerifier(accepted=False)
    verifier = NamedSubjectVerifier(base)

    assert verifier("nature Mount Everest summit mountain", SimpleNamespace()) is False


def test_generic_query_keeps_normal_verification_behavior():
    base = StubVerifier(accepted=True, uncertain=True)
    verifier = NamedSubjectVerifier(base)

    assert verifier("technology fiber optic cable light data", SimpleNamespace()) is True
    assert "NAMED-SUBJECT IDENTITY REQUIREMENT" not in base.seen_query
