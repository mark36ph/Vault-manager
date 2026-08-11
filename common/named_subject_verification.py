"""Stricter visual verification for named subjects and scene transitions.

This wrapper keeps the existing topic-neutral verifier while preserving named
entities as complete semantic subjects. It rejects clear identity contradictions,
but does not require stock footage to prove a landmark or place from pixels alone.
Ambiguous but plausible imagery remains marked uncertain so the acquisition layer
can keep searching and use it only as a fallback.

Candidates may also carry the previous scene query in metadata. When they do, the
visual verifier is explicitly asked to judge the pixels for the current scene and
reject imagery that is clearly more characteristic of the previous scene. This
catches stale-scene bleed that provider titles and tags cannot identify.
"""
from __future__ import annotations

import re
from typing import Any, Mapping


_BROAD_ANCHORS = {
    "space", "science", "nature", "history", "technology", "engineering",
    "health", "medicine", "animals", "animal", "ocean", "geography",
    "physics", "chemistry", "biology", "astronomy", "earth", "environment",
    "transport", "architecture", "geology",
}

_GENERIC_CAPITALIZED = {
    "Aerial", "Close", "Documentary", "Realistic", "Scientific", "Vertical",
    "Wide", "Landscape", "Photo", "Photography", "Video", "Image",
}

_TWO_WORD_PREFIXES = {
    "Mount", "Mt", "Mauna", "Lake", "Cape", "Fort", "Saint", "St",
}

_ENTITY_TERMINALS = {
    "Bridge", "Reef", "Telescope", "Station", "Tower", "Building", "Palace",
    "Temple", "Cathedral", "Church", "Mosque", "River", "Sea", "Ocean",
    "Lake", "Island", "Falls", "Dam", "Park", "City", "University", "Museum",
    "Airport", "Volcano", "Mountain", "Peak", "Monument", "Castle", "Canal",
    "Desert", "Forest", "Bay", "Gulf", "Peninsula", "Spacecraft", "Rover",
    "Tomb", "Wall", "Capitol", "Center", "Centre",
}

_PREVIOUS_QUERY_METADATA_KEY = "_selection_previous_query"


def _normalized_words(value: str) -> str:
    return " ".join(re.findall(r"[a-z0-9]+", str(value or "").casefold()))


def _trim_named_run(tokens: list[str]) -> list[str]:
    if not tokens:
        return []

    if tokens[0] in _TWO_WORD_PREFIXES and len(tokens) >= 2:
        return tokens[:2]

    for index, token in enumerate(tokens):
        if token in _ENTITY_TERMINALS:
            return tokens[: index + 1]

    # Consecutive proper nouns often include a following region/country in stock
    # queries (for example "Mauna Loa Hawaii"). Without an explicit terminal,
    # two words is the safest general-purpose entity boundary.
    return tokens[:2]


def named_subject_phrase(query: str) -> str:
    """Return the strongest proper-named phrase visible in a stock search query.

    Imported searches preserve proper-name capitalization, which lets us retain
    multiword entities such as ``Mauna Loa``, ``Mount Everest``, ``Great Barrier
    Reef``, and ``James Webb Space Telescope`` as one semantic subject instead of
    reducing them to a single keyword.
    """
    tokens = re.findall(r"[A-Za-z0-9][A-Za-z0-9'’-]*", str(query or ""))
    if not tokens:
        return ""

    if tokens[0].casefold() in _BROAD_ANCHORS:
        tokens = tokens[1:]

    runs: list[list[str]] = []
    current: list[str] = []
    for token in tokens:
        is_named = (
            len(token) > 1
            and (token[0].isupper() or token.isupper())
            and token not in _GENERIC_CAPITALIZED
        )
        if is_named:
            current.append(token)
        elif current:
            runs.append(current)
            current = []
    if current:
        runs.append(current)

    if not runs:
        return ""

    candidates = [_trim_named_run(run) for run in runs]
    best = max(candidates, key=lambda run: (len(run), len(" ".join(run))))
    return " ".join(best).strip()


def _previous_scene_query(asset: Any) -> str:
    candidate = getattr(asset, "candidate", None)
    metadata = getattr(candidate, "metadata", None)
    if not isinstance(metadata, Mapping):
        return ""
    return str(metadata.get(_PREVIOUS_QUERY_METADATA_KEY) or "").strip()


def _transition_instruction(current_query: str, previous_query: str) -> str:
    """Build a topic-neutral visual continuity instruction for a real transition."""
    if not previous_query:
        return ""
    if _normalized_words(current_query) == _normalized_words(previous_query):
        return ""

    current_entity = named_subject_phrase(current_query)
    previous_entity = named_subject_phrase(previous_query)
    named_transition = bool(
        current_entity
        and previous_entity
        and _normalized_words(current_entity) != _normalized_words(previous_entity)
    )

    instruction = (
        "\n\nSCENE-TRANSITION RELEVANCE REQUIREMENT:\n"
        f"CURRENT SCENE: {current_query}\n"
        f"PREVIOUS SCENE: {previous_query}\n"
        "Judge this asset primarily for the CURRENT scene. Inspect the visible "
        "content itself, not just stock metadata. If the asset is clearly more "
        "characteristic of the PREVIOUS scene than the CURRENT scene, reject it "
        "as stale-scene imagery. This includes recognizable subject, location, "
        "environment, object type, species, architecture, landform, action, or "
        "other visual identity carried over from the previous scene. Do not reject "
        "neutral imagery merely because it could fit both scenes, and do not reject "
        "legitimate continuity when the current scene still visually calls for the "
        "same subject. Prefer visible evidence that supports the current subject, "
        "setting, form, action, or comparison. When uncertain but still plausibly "
        "current-scene imagery, keep the normal uncertainty behavior rather than "
        "inventing a contradiction."
    )

    if named_transition:
        instruction += (
            "\n\nHARD NAMED-SUBJECT TRANSITION REQUIREMENT: "
            f"The narration has moved from '{previous_entity}' to '{current_entity}'. "
            "For this explicit subject change, visible features that are strongly or "
            "signature-characteristic of the PREVIOUS named subject are a contradiction "
            "when those features are not also called for by the CURRENT scene. Reject "
            "such stale imagery even if it shares the same broad category or theme. "
            "Generic topical overlap alone is not enough to pass. A neutral asset may "
            "still pass when it does not visibly pull the viewer back to the previous "
            "subject, and the current named subject still does not require impossible "
            "pixel-only proof."
        )

    return instruction


class NamedSubjectVerifier:
    """Proxy an existing verifier with named-identity and scene-context semantics."""

    def __init__(self, base_verifier: Any) -> None:
        self.base_verifier = base_verifier

    def __getattr__(self, name: str) -> Any:
        return getattr(self.base_verifier, name)

    def __call__(self, query: str, asset: Any) -> bool:
        entity = named_subject_phrase(query)
        check_query = str(query or "").strip()

        if entity:
            check_query += (
                "\n\nNAMED-SUBJECT IDENTITY REQUIREMENT: "
                f"The requested named subject is '{entity}'. Judge it as that complete "
                "entity rather than as separate matching keywords. Reject clear evidence "
                "of a different named subject or a different semantic meaning. However, "
                "do not demand impossible pixel-only proof for visually similar places, "
                "landforms, buildings, species, machines, or celestial bodies. Stock "
                "metadata may corroborate a visually plausible match, but it must never "
                "override a visible contradiction. If the image is plausible for the "
                "requested entity but cannot be uniquely identified from pixels, keep it "
                "and mark subject uncertainty rather than calling it wrong_named_subject. "
                "The acquisition system will prefer a more certain candidate when one is "
                "available. Apply this principle to all named people, places, landmarks, "
                "species, machines, vehicles, products, celestial bodies, organizations, "
                "and other concrete named entities."
            )

        current_query = check_query.split("\n\n", 1)[0]
        check_query += _transition_instruction(current_query, _previous_scene_query(asset))
        return bool(self.base_verifier(check_query, asset))


__all__ = ["NamedSubjectVerifier", "named_subject_phrase"]
