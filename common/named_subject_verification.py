"""Stricter visual verification for concrete named subjects.

This wrapper keeps the existing topic-neutral verifier, but makes identity checks
more conservative when a scene names a specific person, place, landmark, machine,
species, celestial body, organization, or other proper-named subject. Generic
same-class stock imagery is not good enough for a named subject if it could just
as easily depict a different entity.
"""
from __future__ import annotations

import re
from typing import Any


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


class NamedSubjectVerifier:
    """Proxy an existing visual verifier with strict named-identity semantics."""

    def __init__(self, base_verifier: Any) -> None:
        self.base_verifier = base_verifier

    def __getattr__(self, name: str) -> Any:
        return getattr(self.base_verifier, name)

    def __call__(self, query: str, asset: Any) -> bool:
        entity = named_subject_phrase(query)
        check_query = str(query or "").strip()

        if entity:
            check_query += (
                "\n\nSTRICT NAMED-SUBJECT IDENTITY REQUIREMENT: "
                f"The requested named subject is '{entity}'. Judge the visible subject as "
                "that complete entity, not as separate matching keywords. A generic member "
                "of the same class is not an acceptable substitute. For example, a generic "
                "volcano cannot stand in for a specifically named volcano, a generic mountain "
                "cannot stand in for a named peak, and a generic bridge cannot stand in for a "
                "named bridge. If the visual could just as plausibly depict a different member "
                "of the same class and there is no visible evidence supporting the requested "
                "identity, reject it as wrong_named_subject or mark the subject uncertain. "
                "Do not use stock metadata, filenames, tags, or the repeated query wording as "
                "proof of identity. Apply this rule to all named places, people, landmarks, "
                "species, machines, vehicles, products, celestial bodies, organizations, and "
                "other concrete named entities."
            )

        accepted = bool(self.base_verifier(check_query, asset))

        if entity and accepted and bool(
            getattr(self.base_verifier, "last_subject_uncertain", False)
        ):
            self.base_verifier.last_decision = (
                f"named subject identity uncertain: {entity}"
            )
            return False

        return accepted


__all__ = ["NamedSubjectVerifier", "named_subject_phrase"]
